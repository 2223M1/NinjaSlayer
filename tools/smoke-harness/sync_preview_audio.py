"""Align recorded FMOD output to captured frames using actual source waveforms.

Requires ffmpeg, NumPy and SciPy. This only changes exported media timestamps.
The preview must contain at least two identifiable Ninja Slayer attack/hurt cues.
"""

import argparse
import bisect
import csv
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

import numpy as np
from scipy.signal import correlate


SAMPLE_RATE = 48000
REPOSITORY = Path(__file__).resolve().parents[2]
SOURCES = {
    "ninja_slayer_slow_attack": "ninja_slayer_attack.wav",
    "ninja_slayer_hurt": "ninja_slayer_hurt.wav",
}


def read_audio(path):
    data = subprocess.check_output([
        "ffmpeg", "-v", "error", "-i", str(path), "-f", "f32le",
        "-ac", "1", "-ar", str(SAMPLE_RATE), "-",
    ])
    return np.frombuffer(data, dtype=np.float32).astype(np.float64)


def locate_waveform(samples, template, lower, upper):
    window = samples[lower:upper]
    if len(window) < len(template):
        return None
    correlation = correlate(window, template, mode="valid", method="fft")
    cumulative = np.concatenate(([0.0], np.cumsum(window * window)))
    energy = cumulative[len(template):] - cumulative[:-len(template)]
    scores = correlation / np.sqrt(np.maximum(energy * np.dot(template, template), 1e-20))
    best = int(np.argmax(scores))
    return (lower + best) / SAMPLE_RATE, float(scores[best])


def source_templates(project):
    paths = {name: {REPOSITORY / "NinjaSlayer/audio/sources" / source}
             for name, source in SOURCES.items()}
    # FMOD multi-sounds choose among distinct recordings of the same event.
    # Read their source assets instead of assuming the legacy waveform is chosen.
    for event_path in (project / "Metadata/Event").glob("*.xml"):
        event = ET.parse(event_path).getroot()
        name = event.findtext("object[@class='Event']/property[@name='name']/value")
        if name not in paths:
            continue
        for reference in event.findall(".//relationship[@name='audioFile']/destination"):
            metadata = ET.parse(project / "Metadata/AudioFile" / f"{reference.text}.xml")
            asset = metadata.findtext("object/property[@name='assetPath']/value")
            if not asset:
                raise RuntimeError(f"Missing FMOD asset path: {reference.text}")
            paths[name].add(project / "Assets" / asset)
    return {name: {str(path): read_audio(path) for path in sorted(variants)}
            for name, variants in paths.items()}


def synchronize(directory, output, fmod_project):
    video = json.loads((directory / "recording-start.json").read_text())
    audio = json.loads((directory / "audio-start.json").read_text())
    start = video["timestamp"] / video["frequency"]
    offset = start - audio["seconds"]
    with (directory / "video-frames.csv").open(newline="") as handle:
        frames = [(int(row["qpc"]), int(row["frame"])) for row in csv.DictReader(handle)]
    frame_times = [row[0] for row in frames]
    samples = read_audio(directory / "audio.wav")
    templates = source_templates(fmod_project)
    matches = []
    for line in (directory / "audio-events.jsonl").read_text().splitlines():
        event = json.loads(line)
        name = event["event"].rsplit("/", 1)[-1]
        if name not in templates:
            continue
        frame_index = bisect.bisect_left(frame_times, event["qpc"])
        if frame_index == len(frames):
            continue
        cue = event["qpc"] / video["frequency"] - audio["seconds"]
        lower = max(0, round((cue - 0.08) * SAMPLE_RATE))
        candidates = []
        for source, template in templates[name].items():
            upper = min(len(samples), round((cue + 0.5) * SAMPLE_RATE) + len(template))
            located = locate_waveform(samples, template, lower, upper)
            if located is not None:
                candidates.append((located[1], located[0], source))
        if not candidates:
            continue
        score, located_time, source = max(candidates)
        if score < 0.3:
            continue
        sound_time = located_time - offset
        frame = frames[frame_index][1]
        matches.append({"event": name, "videoFrame": frame, "videoSeconds": frame / 60,
                        "audioSeconds": sound_time, "correlation": score, "source": source,
                        "delaySeconds": sound_time - frame / 60})
    if len(matches) < 2:
        raise RuntimeError("Insufficient waveform matches; audio sync has not been verified.")
    delays = np.array([match["delaySeconds"] for match in matches])
    median = float(np.median(delays))
    unique = []
    for match in sorted(matches, key=lambda match: abs(match["delaySeconds"] - median)):
        if not any(abs(other["audioSeconds"] - match["audioSeconds"]) < 0.002 for other in unique):
            unique.append(match)
    matches = sorted(unique, key=lambda match: match["videoFrame"])
    delays = np.array([match["delaySeconds"] for match in matches])
    median = float(np.median(delays))
    tolerance = max(0.02, 3 * float(np.median(np.abs(delays - median))))
    accepted = [match for match in matches if abs(match["delaySeconds"] - median) <= tolerance]
    if len(accepted) < 2:
        raise RuntimeError("Audio latency is inconsistent across the recording.")
    compensation = float(np.median([match["delaySeconds"] for match in accepted]))
    for match in matches:
        match["residualSeconds"] = match["delaySeconds"] - compensation
        match["accepted"] = match in accepted
    residual = max(abs(match["residualSeconds"]) for match in accepted)
    if residual > 0.04:
        raise RuntimeError(f"Audio drift exceeds 40 ms after alignment: {residual:.4f}s.")
    packets = [json.loads(line) for line in (directory / "audio-packets.jsonl").read_text().splitlines()]
    packet_error = max(abs(packet["writtenFrames"] / audio["sampleRate"]
                           - (packet["qpc"] - packets[0]["qpc"]) / 10_000_000) for packet in packets)
    if packet_error > 0.004:
        raise RuntimeError(f"Audio packet timeline drift exceeds 4 ms: {packet_error:.4f}s.")
    report = {"timestampSource": audio["timestampSource"], "clockOffsetSeconds": offset,
              "playbackLatencySeconds": compensation, "maximumResidualSeconds": residual,
              "maximumPacketClockErrorSeconds": packet_error,
              "matches": matches}
    if output:
        subprocess.run([
            "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
            "-i", str(directory / "video.mp4"), "-ss", f"{offset + compensation:.9f}",
            "-i", str(directory / "audio.wav"), "-map", "0:v:0", "-map", "1:a:0",
            "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest",
            "-movflags", "+faststart", str(output),
        ], check=True)
        encoded = read_audio(output)
        for match in accepted:
            template = templates[match["event"]][match["source"]]
            cue = match["videoSeconds"]
            located = locate_waveform(encoded, template, max(0, round((cue - 0.08) * SAMPLE_RATE)),
                                      min(len(encoded), round((cue + 0.08) * SAMPLE_RATE) + len(template)))
            if located is None or located[1] < 0.3 or abs(located[0] - cue) > 0.04:
                raise RuntimeError("The encoded output failed audio/frame synchronization verification.")
            match["encodedResidualSeconds"] = located[0] - cue
        report["maximumEncodedResidualSeconds"] = max(abs(match["encodedResidualSeconds"]) for match in accepted)
    (directory / "audio-sync.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"Audio sync: {len(accepted)} source matches, latency {compensation * 1000:.1f} ms, "
          f"maximum residual {residual * 1000:.1f} ms.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--fmod-project", type=Path,
                        default=REPOSITORY.parent / "STS2_FModProject_Minimal-main")
    arguments = parser.parse_args()
    synchronize(arguments.directory.resolve(), arguments.output, arguments.fmod_project)
