extends SceneTree
func _initialize() -> void:
	call_deferred("capture")
func capture() -> void:
	root.size = Vector2i(1920, 1080)
	root.transparent_bg = true
	var popup = load("res://yukano_popup.gd").new()
	root.add_child(popup)
	popup.play()
	await create_timer(0.9).timeout
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://runtime-playing.png")
	await create_timer(0.86).timeout
	assert(not popup.content.visible and not popup.frozen.visible)
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://runtime-exiting.png")
	await create_timer(0.3).timeout
	assert(not popup.active)
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://runtime-hidden.png")
	popup.queue_free()
	print("YUKANO_VISUAL_CAPTURE_PASS")
	quit(0)
