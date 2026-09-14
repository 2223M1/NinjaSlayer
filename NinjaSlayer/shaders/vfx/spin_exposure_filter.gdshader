shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform float sample_step;
uniform bool planar_motion = false;
uniform vec2 planar_pivot;
uniform vec3 planar_sweep;
uniform vec4 capture_rect;

void fragment() {
    vec4 sum = vec4(0.0);
    float coverage = 0.0;
    for (int x = -4; x <= 4; x++) {
        float weight = exp(-0.125 * float(x * x));
        vec2 direction = vec2(1.0, 0.0);
        if (planar_motion) {
            vec2 radial = capture_rect.xy + UV * capture_rect.zw - planar_pivot;
            vec2 flow = vec2(-radial.y, radial.x) * planar_sweep.z + planar_sweep.xy;
            direction = flow / max(length(flow), 1.0);
            direction.y *= capture_rect.z / capture_rect.w;
        }
        vec2 uv = UV + direction * float(x) * sample_step;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) { continue; }
        vec4 pixel = texture(TEXTURE, uv);
        sum += vec4(pixel.rgb * pixel.a, pixel.a) * weight;
        coverage = max(coverage, pixel.a * weight);
    }
    COLOR = vec4(sum.a > 0.0 ? sum.rgb / sum.a : vec3(0.0), coverage);
}
