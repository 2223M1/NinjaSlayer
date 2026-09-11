shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform float sample_ratios[25];
uniform sampler2D body_texture : source_color, filter_linear, repeat_disable;
uniform bool body_premultiplied = false;
uniform float axis_x;
uniform vec4 sprite_rect;
uniform vec2 draw_bounds;
uniform vec2 flipped;
uniform float radiance_power;
varying vec2 local_position;

void vertex() {
    float u = (VERTEX.x - sprite_rect.x) / sprite_rect.z;
    VERTEX.x = mix(draw_bounds.x, draw_bounds.y, u);
    local_position = VERTEX;
}

void fragment() {
    vec4 sum = vec4(0.0);
    float coverage = 0.0;
    for (int i = 0; i < 25; i++) {
        float ratio = sample_ratios[i];
        if (abs(ratio) <= 0.00001) { continue; }
        vec2 point = vec2(axis_x + (local_position.x - axis_x) / ratio, local_position.y);
        vec2 uv = (point - sprite_rect.xy) / sprite_rect.zw;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) { continue; }
        vec4 pixel = texture(body_texture, mix(uv, vec2(1.0) - uv, flipped));
        if (body_premultiplied && pixel.a > 0.0) { pixel.rgb /= pixel.a; }
        sum += vec4(pow(pixel.rgb, vec3(radiance_power)) * pixel.a, pixel.a);
        coverage = max(coverage, pixel.a);
    }
    // Keep energy encoded until the spatial pass. Bright mask/cloth strokes
    // persist through the exposure instead of averaging into the dark body.
    COLOR = vec4(sum.a > 0.0 ? sum.rgb / sum.a : vec3(0.0), coverage);
}
