extends Node

const GRID_SIZE := 10
const VIEWPORT_SIZE := Vector2i(160, 160)
const MESH_SIZE := Vector2(72.0, 72.0)

var _viewport: SubViewport
var _material: ShaderMaterial


func _ready() -> void:
	call_deferred("_run")


func _run() -> void:
	var readback_orientation := await _probe_readback_orientation()
	if readback_orientation == 0:
		push_error("GPU_SENTINEL_FAIL readback orientation marker was ambiguous")
		get_tree().quit(2)
		return

	_setup_surface()
	await _render_once()
	var baseline := _alpha_bounds(_viewport.get_texture().get_image())

	var offsets: Array[Vector2] = []
	offsets.resize(16)
	for index in offsets.size():
		var column := index % 4
		var row := index / 4
		var normalized_y := (float(row) / 3.0) * 2.0 - 1.0
		offsets[index] = Vector2(float(column) * 7.0, normalized_y * float(column) * 4.0)
	_material.set_shader_parameter("control_offsets", offsets)
	await _render_once()
	var deformed := _alpha_bounds(_viewport.get_texture().get_image())

	var width_growth := deformed.size.x - baseline.size.x
	var centroid_shift := deformed.get_center().x - baseline.get_center().x
	if width_growth >= 14.0 and centroid_shift >= 7.0:
		var orientation_label := "normal" if readback_orientation > 0 else "flipped"
		print(
			"GPU_SENTINEL_PASS baseline=", baseline,
			" deformed=", deformed,
			" readback_orientation=", orientation_label)
		get_tree().quit(0)
		return

	push_error("GPU_SENTINEL_FAIL baseline=%s deformed=%s" % [baseline, deformed])
	get_tree().quit(2)


func _probe_readback_orientation() -> int:
	var viewport := SubViewport.new()
	viewport.name = "ReadbackOrientationViewport"
	viewport.size = Vector2i(8, 8)
	viewport.transparent_bg = true
	viewport.disable_3d = true
	viewport.render_target_clear_mode = SubViewport.CLEAR_MODE_ALWAYS
	viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	add_child(viewport)

	var marker := ColorRect.new()
	marker.position = Vector2.ZERO
	marker.size = Vector2(2.0, 2.0)
	marker.color = Color.WHITE
	marker.mouse_filter = Control.MOUSE_FILTER_IGNORE
	viewport.add_child(marker)
	viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	await RenderingServer.frame_post_draw
	await get_tree().process_frame
	var image := viewport.get_texture().get_image()
	var top_alpha := image.get_pixel(0, 0).a
	var bottom_alpha := image.get_pixel(0, image.get_height() - 1).a
	viewport.queue_free()
	if top_alpha >= 0.75 and bottom_alpha <= 0.25:
		return 1
	if bottom_alpha >= 0.75 and top_alpha <= 0.25:
		return -1
	return 0


func _setup_surface() -> void:
	_viewport = SubViewport.new()
	_viewport.name = "SentinelViewport"
	_viewport.size = VIEWPORT_SIZE
	_viewport.transparent_bg = true
	_viewport.disable_3d = true
	_viewport.render_target_clear_mode = SubViewport.CLEAR_MODE_ALWAYS
	_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	add_child(_viewport)

	var mesh_node := MeshInstance2D.new()
	mesh_node.position = Vector2(VIEWPORT_SIZE) * 0.5
	mesh_node.mesh = _build_mesh()
	mesh_node.texture = _build_texture()
	_material = ShaderMaterial.new()
	_material.shader = load("res://NinjaSlayer/shaders/vfx/boss_dismemberment_clip.gdshader")
	_material.set_shader_parameter("seed_count", 0)
	_material.set_shader_parameter("cell_seed", Vector2.ZERO)
	_material.set_shader_parameter("cell_bounds_min", -MESH_SIZE * 0.5)
	_material.set_shader_parameter("cell_bounds_size", MESH_SIZE)
	_material.set_shader_parameter("part_bounds_min", -MESH_SIZE * 0.5)
	_material.set_shader_parameter("part_bounds_size", MESH_SIZE)
	_material.set_shader_parameter("atlas_content_min", Vector2.ZERO)
	_material.set_shader_parameter("atlas_content_size", Vector2.ONE)
	for index in 24:
		_material.set_shader_parameter("seed_%d" % index, Vector2.ZERO)
	var offsets: Array[Vector2] = []
	offsets.resize(16)
	for index in offsets.size():
		offsets[index] = Vector2.ZERO
	_material.set_shader_parameter("control_offsets", offsets)
	mesh_node.material = _material
	_viewport.add_child(mesh_node)


func _build_mesh() -> ArrayMesh:
	var vertices := PackedVector2Array()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()
	for row in GRID_SIZE:
		for column in GRID_SIZE:
			var uv := Vector2(float(column), float(row)) / float(GRID_SIZE - 1)
			vertices.append((uv - Vector2(0.5, 0.5)) * MESH_SIZE)
			uvs.append(uv)
	for row in GRID_SIZE - 1:
		for column in GRID_SIZE - 1:
			var top_left := row * GRID_SIZE + column
			var top_right := top_left + 1
			var bottom_left := top_left + GRID_SIZE
			var bottom_right := bottom_left + 1
			indices.append_array(PackedInt32Array([
				top_left, top_right, bottom_right,
				top_left, bottom_right, bottom_left,
			]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _build_texture() -> ImageTexture:
	var image := Image.create(8, 8, false, Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	return ImageTexture.create_from_image(image)


func _render_once() -> void:
	_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	await RenderingServer.frame_post_draw
	await get_tree().process_frame


func _alpha_bounds(image: Image) -> Rect2:
	var minimum := Vector2i(image.get_width(), image.get_height())
	var maximum := Vector2i(-1, -1)
	for y in image.get_height():
		for x in image.get_width():
			if image.get_pixel(x, y).a <= 0.1:
				continue
			minimum.x = mini(minimum.x, x)
			minimum.y = mini(minimum.y, y)
			maximum.x = maxi(maximum.x, x)
			maximum.y = maxi(maximum.y, y)
	if maximum.x < minimum.x or maximum.y < minimum.y:
		return Rect2()
	return Rect2(Vector2(minimum), Vector2(maximum - minimum + Vector2i.ONE))
