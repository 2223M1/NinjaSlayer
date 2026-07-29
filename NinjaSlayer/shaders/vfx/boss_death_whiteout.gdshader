shader_type canvas_item;

uniform float white_mix : hint_range(0.0, 1.0) = 0.0;

varying vec4 modulate_color;

void vertex() {
    modulate_color = COLOR;
}

void fragment() {
    vec4 color = texture(TEXTURE, UV);
    color.rgb = mix(color.rgb, vec3(1.0), white_mix);
    COLOR = color * modulate_color;
}
