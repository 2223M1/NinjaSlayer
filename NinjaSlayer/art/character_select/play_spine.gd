extends SpineSprite

func _ready():
	if get_animation_state() != null:
		get_animation_state().set_animation("animation", true, 0)
	else:
		call_deferred("start_animation")

func start_animation():
	assert(get_animation_state() != null, "Spine animation state missing")
	get_animation_state().set_animation("animation", true, 0)
