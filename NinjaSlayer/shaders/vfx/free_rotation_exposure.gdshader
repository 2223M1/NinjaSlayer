shader_type canvas_item;
render_mode unshaded;

uniform vec4 samples_x[25];
uniform vec4 samples_y[25];
uniform vec4 sprite_rect;
uniform vec4 draw_rect;
uniform vec2 flipped;
uniform float radiance_power = 1.0;
varying vec2 point;

void vertex() {
    vec2 uv = (VERTEX - sprite_rect.xy) / sprite_rect.zw;
    VERTEX = draw_rect.xy + uv * draw_rect.zw;
    point = VERTEX;
}

void fragment() {
    vec4 tint = COLOR;
    vec4 sum = vec4(0.0);
    float coverage = 0.0;
    for (int i = 0; i < 25; i++) {
        vec3 p = vec3(point, 1.0);
        vec2 uv = (vec2(dot(samples_x[i].xyz, p), dot(samples_y[i].xyz, p)) - sprite_rect.xy) / sprite_rect.zw;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) { continue; }
        vec4 pixel = texture(TEXTURE, mix(uv, 1.0 - uv, flipped));
        float weight = 1.0 - float(i) / 32.0;
        sum += vec4(pow(pixel.rgb, vec3(radiance_power)) * pixel.a, pixel.a) * weight;
        coverage = max(coverage, pixel.a * weight);
    }
    vec3 radiance = sum.a > 0.0 ? pow(sum.rgb / sum.a, vec3(1.0 / radiance_power)) : vec3(0.0);
    COLOR = tint * vec4(radiance, coverage * 0.65);
}
