extends Node2D
signal finished
const FPS := 24000.0 / 1001.0
const ENTRY := 5.0 / FPS
const FILM := 36.0 / FPS
const EXIT := 4.0 / FPS
var elapsed := 0.0
var extra_hold := 0.0
var active := false
var film_started := false
var drawing_index := -1
var atlas: Texture2D
var entries: Array = []
var viewport: SubViewport
var film: VideoStreamPlayer
var frozen: Sprite2D
var content: Sprite2D

func _ready() -> void:
	var folder: String = get_script().resource_path.get_base_dir()
	atlas = load(folder.path_join("runtime-atlas.png"))
	entries = JSON.parse_string(FileAccess.get_file_as_string(folder.path_join("animation.json"))).entries
	viewport = SubViewport.new()
	viewport.size = Vector2i(1920, 1080)
	viewport.transparent_bg = true
	viewport.audio_listener_enable_2d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	add_child(viewport)
	film = VideoStreamPlayer.new()
	film.stream = load(folder.path_join("yukano-original.ogv"))
	film.expand = true
	film.loop = false
	film.position = Vector2(-430, -240)
	film.size = Vector2(1824, 1026)
	film.mouse_filter = Control.MOUSE_FILTER_IGNORE
	viewport.add_child(film)
	frozen = Sprite2D.new()
	frozen.texture = load(folder.path_join("last-frame.png"))
	frozen.centered = false
	frozen.position = film.position
	frozen.scale = Vector2(0.95, 0.95)
	frozen.visible = false
	viewport.add_child(frozen)
	film.finished.connect(func():
		if extra_hold > 0.0 and active and content.visible:
			frozen.visible = true
		else:
			close()
	)
	content = Sprite2D.new()
	content.centered = false
	content.texture = viewport.get_texture()
	content.z_index = -1
	var clipping := ShaderMaterial.new()
	clipping.shader = load(folder.path_join("clip.gdshader"))
	clipping.set_shader_parameter("interior_mask", load(folder.path_join("interior-clip.png")))
	content.material = clipping
	content.visible = false
	add_child(content)
	set_process(false)

func play(extra_hold_seconds: float = 0.0) -> void:
	assert(extra_hold_seconds >= 0.0)
	film.stop()
	frozen.visible = false
	content.visible = false
	extra_hold = extra_hold_seconds
	elapsed = 0.0
	film_started = false
	active = true
	visible = true
	drawing_index = 0
	set_process(true)
	queue_redraw()

func close() -> void:
	if active:
		elapsed = ENTRY + FILM + extra_hold
		_process(0.0)

func _process(delta: float) -> void:
	if not active:
		return
	elapsed += delta
	if elapsed < ENTRY:
		drawing_index = mini(4, int(elapsed * FPS))
	elif elapsed < ENTRY + FILM + extra_hold:
		drawing_index = 5
		content.visible = true
		if not film_started:
			film.play()
			film_started = true
	elif elapsed < ENTRY + FILM + extra_hold + EXIT:
		content.visible = false
		film.stop()
		drawing_index = 6 + mini(3, int((elapsed - ENTRY - FILM - extra_hold) * FPS))
	else:
		film.stop()
		content.visible = false
		active = false
		visible = false
		drawing_index = -1
		set_process(false)
		finished.emit()
	queue_redraw()

func _draw() -> void:
	if drawing_index >= 0:
		var e: Dictionary = entries[drawing_index]
		var r: Array = e.atlas_rect
		var p: Array = e.source_origin
		draw_texture_rect_region(atlas, Rect2(p[0], p[1], r[2], r[3]), Rect2(r[0], r[1], r[2], r[3]))
