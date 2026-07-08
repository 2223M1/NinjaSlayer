@tool
extends EditorScript

func _run() -> void:
	var paths := PackedStringArray([
		"res://NinjaSlayer/animations/character_select/ninja_slayer/characterselect_ninja_slayer_bg.atlas",
		"res://NinjaSlayer/animations/character_select/ninja_slayer/characterselect_ninja_slayer_char.atlas",
		"res://NinjaSlayer/animations/character_select/ninja_slayer/characterselect_ninja_slayer.skel",
	])
	EditorInterface.get_resource_filesystem().reimport_files(paths)
	print("Requested spine reimport for ", paths.size(), " files")
