"""Shorten progressive scarf extension while retaining its canvas-space width envelope."""
import numpy as np
from PIL import Image

EXTENSION = 140.0


def resize_tail(rig, profile, texture, extension=EXTENSION):
    mesh = rig['skins'][0]['attachments']['tail']['tail']
    alpha = np.asarray(Image.open(texture))[:, :, 3]
    px = np.asarray(profile['envelope_source_x'])
    top = np.asarray(profile['envelope_top'])
    bottom = np.asarray(profile['envelope_bottom'])
    raw_top, raw_bottom = [], []
    for x in px.astype(int):
        rows = np.flatnonzero(alpha[:, x] > profile['envelope_alpha_threshold'])
        raw_top.append(rows[0] - .5)
        raw_bottom.append(rows[-1] + .5)
    end = profile['source_end_x']
    fixed = profile['fixed_through_source_x']
    uv = np.asarray(mesh['uvs']).reshape(-1, 2) * [mesh['width'], mesh['height']]
    v = mesh['vertices']
    assert len(v) == len(uv) * 5
    for i, (x, y) in enumerate(uv):
        offset = 5 * i
        assert v[offset] == 1 and v[offset + 4] == 1
        assert rig['bones'][v[offset + 1]]['name'] == 'scarf'
        if x <= fixed:
            continue  # Preserve the exact approved collar contact vertices.
        dest_x = x + extension * ((x - fixed) / (end - fixed)) ** 2
        lo, hi = np.interp(x, px, raw_top), np.interp(x, px, raw_bottom)
        edges = []
        for edge, slope in zip((top, bottom), profile['beyond_crop_edge_slopes']):
            edges.append(np.interp(dest_x, px, edge) if dest_x <= end
                         else edge[-1] + slope * (dest_x - end))
        dest_y = edges[0] + (y - lo) * (edges[1] - edges[0]) / (hi - lo)
        v[offset + 2] = round(float(dest_x - profile['anchor'][0]), 6)
        v[offset + 3] = round(float(-dest_y + profile['anchor'][1]), 6)
