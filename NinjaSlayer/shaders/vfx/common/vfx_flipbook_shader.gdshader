shader_type canvas_item;
render_mode blend_mix;

#include "res://NinjaSlayer/shaders/vfx/_util/flipbook.gdshaderinc"
#include "res://NinjaSlayer/shaders/vfx/_util/hue_shift.gdshaderinc"
#include "res://NinjaSlayer/shaders/vfx/_util/erosion_from_factors.gdshaderinc"

uniform vec2 pivot_offset = vec2(0.0, 0.0);

uniform sampler2D lut : repeat_disable;

uniform float frame_count = 9;
uniform vec2 flipbook_size = vec2(3,3);
uniform sampler2D flipbook_curve : repeat_disable, hint_default_black;

uniform float use_erosion_texture = 0.0;
uniform sampler2D erosion_texture : repeat_disable;
uniform sampler2D erosion_curve : hint_default_black;
uniform float erosion_offset = 0.1;

uniform float use_hue_shift = 0.0;
uniform sampler2D hue_shift_curve : repeat_disable, hint_default_black;

varying float lifetime;
varying float flipbook_offset;
varying vec4 vertex_color;

varying vec2 flipbook_uv;

void vertex() {
	vertex_color = COLOR;
	lifetime = INSTANCE_CUSTOM.y/INSTANCE_CUSTOM.w;
	flipbook_offset = INSTANCE_CUSTOM.z;
	
	VERTEX += pivot_offset * (1.0/TEXTURE_PIXEL_SIZE);
	
	float flipbook_progress = textureLod(flipbook_curve, vec2(lifetime), 0.0).r;
	flipbook_uv = flipbook(UV, flipbook_size, ((frame_count * flipbook_offset) + (frame_count * flipbook_progress)) * 0.99);
}

void fragment() {
	vec4 flipbook_texture_color =  texture(TEXTURE, flipbook_uv);
	vec4 erosion_color = texture(erosion_texture, UV);

	float erosion_curve_val = texture(erosion_curve, vec2(lifetime)).r;
	float erosion_val = erosion_from_factors(vec2(erosion_curve_val, erosion_offset), erosion_color.r);

    float erosion = mix(1.0, erosion_val, use_erosion_texture);
	float alpha = flipbook_texture_color.a * vertex_color.a * erosion;

	//if(alpha < 0.001)
		//discard;
		
	vec4 lut_color = texture(lut, flipbook_texture_color.rr);
	vec4 initial_color = lut_color * vertex_color;

	float hue_shift_val = texture(hue_shift_curve, flipbook_texture_color.rr).r;
	vec4 shifted_color = shift_hue(initial_color, hue_shift_val);
	
    vec4 final_color = mix(initial_color, shifted_color, use_hue_shift);

	COLOR = vec4(final_color.rgb, alpha);
}