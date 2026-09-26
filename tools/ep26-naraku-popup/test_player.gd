extends SceneTree

func _initialize() -> void:
	call_deferred("run_test")

func run_test() -> void:
	var player = load("res://popup_player.gd").new()
	root.add_child(player)
	var ended := [0]
	player.finished.connect(func(): ended[0] += 1)
	for seconds in [0.0, 0.1, 2.0, 20.0]:
		player.play(seconds)
		assert(player.drawing_index == 0)
		player._process(4.5 / player.FPS)
		assert(player.drawing_index == 4)
		player._process(0.5 / player.FPS + 0.000001)
		if seconds > 0.0:
			assert(player.drawing_index == 5)
			player._process(seconds)
		assert(player.drawing_index == 6)
		player._process(3.5 / player.FPS)
		assert(player.drawing_index == 9)
		player._process(0.5 / player.FPS)
		assert(not player.active and not player.visible)
	player.play(-1.0)
	player._process(300.0)
	assert(player.drawing_index == 5)
	player.close()
	assert(player.drawing_index == 6)
	player._process(player.EXIT_SECONDS + 0.000001)
	assert(not player.active)
	assert(ended[0] == 5)
	player.queue_free()
	print("POPUP_PLAYER_PASS: zero/short/long/custom holds, manual close, native enter/exit, hidden at end")
	quit(0)
