shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform vec4 samples_x[129];
uniform vec4 samples_y[129];
uniform sampler2D body_texture : source_color, filter_linear, repeat_disable;
uniform vec4 sprite_rect;
uniform vec4 draw_rect;
uniform vec2 flipped;
uniform float radiance_power;
varying vec2 local_position;

void vertex() {
    vec2 uv = (VERTEX - sprite_rect.xy) / sprite_rect.zw;
    VERTEX = draw_rect.xy + uv * draw_rect.zw;
    local_position = VERTEX;
}

void fragment() {
    vec4 sum = vec4(0.0);
    float coverage = 0.0;
    for (int i = 0; i < 129; i++) {
        vec3 p = vec3(local_position, 1.0);
        vec2 uv = (vec2(dot(samples_x[i].xyz, p), dot(samples_y[i].xyz, p)) - sprite_rect.xy) / sprite_rect.zw;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) { continue; }
        vec4 pixel = texture(body_texture, mix(uv, 1.0 - uv, flipped));
        float age = float(i) / 128.0;
        float persistence = exp(-2.0 * age * age);
        sum += vec4(pow(pixel.rgb, vec3(radiance_power)) * pixel.a, pixel.a) * persistence;
        coverage = max(coverage, pixel.a * persistence);
    }
    COLOR = vec4(sum.a > 0.0 ? sum.rgb / sum.a : vec3(0.0), coverage);
}
