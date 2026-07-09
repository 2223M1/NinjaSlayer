shader_type canvas_item;

uniform float blur_strength = 0.0;
uniform float blur_sign = 1.0;

void fragment() {
	vec4 base = texture(TEXTURE, UV);
	if (blur_strength <= 0.001) {
		COLOR = base * COLOR;
	} else {
		vec2 step = blur_sign * blur_strength * 24.0 / vec2(textureSize(TEXTURE, 0));
		vec3 rgb = base.rgb * 0.26;
		rgb += texture(TEXTURE, UV - step * 1.0).rgb * 0.18;
		rgb += texture(TEXTURE, UV - step * 2.0).rgb * 0.14;
		rgb += texture(TEXTURE, UV - step * 3.0).rgb * 0.10;
		rgb += texture(TEXTURE, UV - step * 4.0).rgb * 0.06;
		rgb += texture(TEXTURE, UV + step * 1.0).rgb * 0.14;
		rgb += texture(TEXTURE, UV + step * 2.0).rgb * 0.08;
		rgb += texture(TEXTURE, UV + step * 3.0).rgb * 0.04;
		COLOR = vec4(rgb, base.a) * COLOR;
	}
}
