shader_type canvas_item;

varying vec4 item_color;
void vertex() {
    item_color = COLOR;
}
void fragment() {
    vec4 texel = texture(TEXTURE, UV);
    // Turn native red ink yellow, retaining shading, white highlights and alpha.
    texel.g += max(texel.r - max(texel.g, texel.b), 0.0) * 0.84;
    COLOR = item_color * texel;
}
