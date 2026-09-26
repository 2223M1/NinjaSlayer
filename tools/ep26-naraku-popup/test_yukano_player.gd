extends SceneTree
var popup: Node2D
func _initialize() -> void:
	call_deferred("run_test")
func run_test() -> void:
	popup = load("res://yukano_popup.gd").new()
	root.add_child(popup)
	for extra in [0.0, 0.1, 10.0]:
		popup.play(extra)
		assert(popup.drawing_index == 0)
		popup._process(popup.ENTRY + 0.000001)
		assert(popup.film_started and popup.content.visible and popup.drawing_index == 5)
		popup._process(popup.FILM + extra)
		assert(not popup.content.visible and popup.drawing_index == 6)
		popup._process(popup.EXIT)
		assert(not popup.active and not popup.visible)
	popup.play()
	assert(popup.extra_hold == 0.0)
	await create_timer(0.8).timeout
	assert(popup.film.is_playing() and not popup.frozen.visible)
	await create_timer(0.96).timeout
	assert(not popup.content.visible and not popup.frozen.visible)
	assert(popup.drawing_index >= 6 and popup.drawing_index <= 9)
	await create_timer(0.2).timeout
	assert(not popup.active and not popup.visible)
	popup.play(1.0)
	await create_timer(0.8).timeout
	assert(popup.film.is_playing())
	assert(popup.film.get_stream_position() > 0.1)
	await create_timer(1.2).timeout
	assert(popup.frozen.visible)
	assert(popup.content.visible)
	await create_timer(1.2).timeout
	assert(not popup.active)
	popup.queue_free()
	print("YUKANO_POPUP_PASS: default has no freeze, immediate exit/hide; explicit optional hold still works")
	quit(0)
