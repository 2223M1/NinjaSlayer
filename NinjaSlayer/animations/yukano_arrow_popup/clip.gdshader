shader_type canvas_item;
uniform sampler2D interior_mask : filter_linear;
void fragment() {
	vec4 video = texture(TEXTURE, UV);
	COLOR = vec4(video.rgb, video.a * texture(interior_mask, UV).r);
}
