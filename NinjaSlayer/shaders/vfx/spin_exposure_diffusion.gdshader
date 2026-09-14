shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform float radius;
uniform bool planar_motion = false;
uniform vec2 planar_pivot;
uniform vec3 planar_sweep;
uniform vec4 capture_rect;

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
        vec2 direction = vec2(0.0, 1.0);
        if (planar_motion) {
            vec2 radial = capture_rect.xy + UV * capture_rect.zw - planar_pivot;
            vec2 flow = vec2(-radial.y, radial.x) * planar_sweep.z + planar_sweep.xy;
            direction = vec2(flow.y, -flow.x) / max(length(flow), 1.0);
            direction.x *= capture_rect.w / capture_rect.z;
        }
        vec2 uv = UV + direction * offset * radius;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) { continue; }
        vec4 pixel = texture(TEXTURE, uv);
        sum += vec4(pixel.rgb * pixel.a, pixel.a) * weight;
    }
    float alpha = sum.a / total_weight;
    COLOR = vec4(sum.a > 0.0 ? sum.rgb / sum.a : vec3(0.0), 1.0 - (1.0 - alpha) * (1.0 - alpha));
}
