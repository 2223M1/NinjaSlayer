extends Node2D
## Transparent, native-cadence frame; play(seconds) controls HOLD only.
signal finished

const FPS := 24000.0 / 1001.0
const ENTER_SECONDS := 5.0 / FPS
const EXIT_SECONDS := 4.0 / FPS
var atlas: Texture2D
var entries: Array = []
var elapsed := 0.0
var hold_seconds := 2.0
var active := false
var drawing_index := -1

func _ready() -> void:
	var folder: String = get_script().resource_path.get_base_dir()
	atlas = load(folder.path_join("runtime-atlas.png"))
	var spec = JSON.parse_string(FileAccess.get_file_as_string(folder.path_join("animation.json")))
	entries = spec.entries
	set_process(false)

func play(seconds: float = 2.0) -> void:
	assert(seconds >= 0.0 or seconds == -1.0, "Use nonnegative seconds, or -1 for manual close.")
	hold_seconds = seconds
	elapsed = 0.0
	active = true
	visible = true
	drawing_index = 0
	set_process(true)
	queue_redraw()

func close() -> void:
	if not active:
		return
	hold_seconds = 0.0
	elapsed = ENTER_SECONDS
	_process(0.0)

func _process(delta: float) -> void:
	if not active:
		return
	elapsed += delta
	if elapsed < ENTER_SECONDS:
		drawing_index = mini(4, int(elapsed * FPS))
	elif hold_seconds < 0.0 or elapsed < ENTER_SECONDS + hold_seconds:
		drawing_index = 5
	elif elapsed < ENTER_SECONDS + hold_seconds + EXIT_SECONDS:
		drawing_index = 6 + mini(3, int((elapsed - ENTER_SECONDS - hold_seconds) * FPS))
	else:
		active = false
		drawing_index = -1
		visible = false
		set_process(false)
		finished.emit()
	queue_redraw()

func _draw() -> void:
	if drawing_index < 0:
		return
	var entry: Dictionary = entries[drawing_index]
	var r: Array = entry.atlas_rect
	var p: Array = entry.source_origin
	draw_texture_rect_region(atlas, Rect2(p[0], p[1], r[2], r[3]), Rect2(r[0], r[1], r[2], r[3]))
