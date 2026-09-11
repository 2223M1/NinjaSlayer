shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform float radius;

void fragment() {
    vec4 sum = vec4(0.0);
    float total_weight = 0.0;
    for (int i = -24; i <= 24; i++) {
        float offset = float(i) / 24.0;
        // A soft core and broader penumbra spread bright strokes vertically as
        // well as horizontally, matching the reference's diffuse persistence.
        float weight = mix(exp(-0.5 * pow(offset * 5.6, 2.0)),
            exp(-0.5 * pow(offset * 2.8, 2.0)), 0.35);
        total_weight += weight;
        vec2 uv = UV + vec2(0.0, offset * radius);
        if (uv.y < 0.0 || uv.y > 1.0) { continue; }
        vec4 pixel = texture(TEXTURE, uv);
        sum += vec4(pixel.rgb * pixel.a, pixel.a) * weight;
    }
    float alpha = sum.a / total_weight;
    COLOR = vec4(sum.a > 0.0 ? sum.rgb / sum.a : vec3(0.0), 1.0 - (1.0 - alpha) * (1.0 - alpha));
}
