shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform float sample_step;

void fragment() {
    vec4 sum = vec4(0.0);
    float coverage = 0.0;
    for (int x = -4; x <= 4; x++) {
        float weight = exp(-0.125 * float(x * x));
        vec2 uv = UV + vec2(float(x) * sample_step, 0.0);
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) { continue; }
        vec4 pixel = texture(TEXTURE, uv);
        sum += vec4(pixel.rgb * pixel.a, pixel.a) * weight;
        coverage = max(coverage, pixel.a * weight);
    }
    COLOR = vec4(sum.a > 0.0 ? sum.rgb / sum.a : vec3(0.0), coverage);
}
