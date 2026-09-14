shader_type canvas_item;
render_mode unshaded;

uniform sampler2D exposure_texture : filter_linear, repeat_disable;
uniform float axis_x;
uniform float current_ratio;
uniform vec4 sprite_rect;
uniform vec4 capture_rect;
uniform vec2 draw_bounds;
uniform vec2 draw_y_bounds;
uniform float blur_radius;
uniform float radiance_power;
uniform bool planar_motion = false;
uniform vec2 planar_pivot;
uniform vec3 planar_sweep;
varying vec2 local_position;
varying vec4 tint;

void vertex() {
    float u = (VERTEX.x - sprite_rect.x) / sprite_rect.z;
    float v = (VERTEX.y - sprite_rect.y) / sprite_rect.w;
    VERTEX.x = mix(draw_bounds.x, draw_bounds.y, u);
    VERTEX.y = mix(draw_y_bounds.x, draw_y_bounds.y, v);
    local_position = vec2(axis_x + (VERTEX.x - axis_x) * current_ratio, VERTEX.y);
    tint = COLOR;
}

void fragment() {
    vec4 sum = vec4(0.0);
    float coverage = 0.0;
    for (int i = 0; i < 65; i++) {
        float offset = float(i - 32) / 32.0;
        float weight = exp(-0.5 * pow(offset * 2.8, 2.0));
        vec2 sample_position = local_position + vec2(offset * blur_radius, 0.0);
        if (planar_motion) {
            vec2 radial = local_position - planar_pivot;
            vec2 flow = vec2(-radial.y, radial.x) * planar_sweep.z + planar_sweep.xy;
            float step = offset * blur_radius / max(length(flow), 1.0);
            float angle = step * planar_sweep.z;
            sample_position = planar_pivot + mat2(vec2(cos(angle), sin(angle)), vec2(-sin(angle), cos(angle))) * radial
                + step * planar_sweep.xy;
        }
        vec2 uv = (sample_position - capture_rect.xy) / capture_rect.zw;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) { continue; }
        vec4 pixel = texture(exposure_texture, uv);
        vec3 light = pow(pixel.rgb, vec3(1.0 / radiance_power));
        float luminance = dot(light, vec3(0.2126, 0.7152, 0.0722));
        float persistence = 1.0 + 6.0 * (radiance_power - 1.0) * pow(luminance, 4.0);
        sum += vec4(pixel.rgb * pixel.a, pixel.a) * weight * persistence;
        coverage = max(coverage, pixel.a * weight);
    }
    // Smear every part, including the stationary spin axis. Retain substantial
    // coverage inside the sweep with soft, bounded horizontal streak ends.
    vec3 color = sum.a > 0.0 ? pow(sum.rgb / sum.a, vec3(1.0 / radiance_power)) : vec3(0.0);
    float highlight = max(dot(color, vec3(0.2126, 0.7152, 0.0722)) - 0.35, 0.0);
    color += vec3(highlight * 0.4 * (radiance_power - 1.0));
    COLOR = vec4(color, coverage) * tint;
}
