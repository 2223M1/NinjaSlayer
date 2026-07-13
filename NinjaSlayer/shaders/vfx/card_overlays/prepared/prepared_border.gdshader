shader_type canvas_item;
render_mode blend_mix;

#include "res://NinjaSlayer/shaders/vfx/_util/erosion_from_factors.gdshaderinc"
#include "res://NinjaSlayer/shaders/vfx/_util/polar_coordinates.gdshaderinc"
#include "res://NinjaSlayer/shaders/vfx/_util/tiling_and_offset.gdshaderinc"

uniform vec4 main_st;
uniform vec2 mask_erosion;

uniform sampler2D lut : repeat_disable;
uniform sampler2D mask : repeat_disable;

varying vec4 vertex_color;

void vertex() {
	vertex_color = COLOR;
}

void fragment() {
	vec2 polar_uv = polar_coordinates(UV, vec2(0.5, 0.5), 1.0, 1.0);
	polar_uv = tiling_and_offset(polar_uv, main_st, vec2(0.0));
	
	vec4 main_tex = texture(TEXTURE, polar_uv);
	vec4 mask_tex = texture(mask, UV);
	
	float alpha = vertex_color.a * mask_tex.a;
	alpha = erosion_from_factors(vec2(mask_erosion.x, mask_erosion.y), alpha);
	
	COLOR = vec4((main_tex * vertex_color).rgb, alpha);
}