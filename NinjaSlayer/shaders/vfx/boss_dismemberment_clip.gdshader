shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform int seed_count = 1;
uniform vec2 cell_seed = vec2(0.0);
uniform vec2 seed_0 = vec2(0.0);
uniform vec2 seed_1 = vec2(0.0);
uniform vec2 seed_2 = vec2(0.0);
uniform vec2 seed_3 = vec2(0.0);
uniform vec2 seed_4 = vec2(0.0);
uniform vec2 seed_5 = vec2(0.0);
uniform vec2 seed_6 = vec2(0.0);
uniform vec2 seed_7 = vec2(0.0);
uniform vec2 seed_8 = vec2(0.0);
uniform vec2 seed_9 = vec2(0.0);
uniform vec2 seed_10 = vec2(0.0);
uniform vec2 seed_11 = vec2(0.0);
uniform vec2 seed_12 = vec2(0.0);
uniform vec2 seed_13 = vec2(0.0);
uniform vec2 seed_14 = vec2(0.0);
uniform vec2 seed_15 = vec2(0.0);
uniform vec2 cell_bounds_min = vec2(-1.0);
uniform vec2 cell_bounds_size = vec2(2.0);
uniform vec2 texture_bounds_min = vec2(-1.0);
uniform vec2 texture_bounds_size = vec2(2.0);
uniform vec2 control_offsets[16];

varying vec2 capture_position;

vec2 control_offset_at(int index) {
    return control_offsets[clamp(index, 0, 15)];
}

void vertex() {
    capture_position = texture_bounds_min + UV * texture_bounds_size;
    vec2 normalized = clamp(
        (capture_position - cell_bounds_min) / max(cell_bounds_size, vec2(0.001)),
        vec2(0.0),
        vec2(1.0));
    vec2 grid = min(normalized * 3.0, vec2(2.9999));
    ivec2 cell = ivec2(floor(grid));
    vec2 blend = fract(grid);
    int top_left = cell.y * 4 + cell.x;
    vec2 top = mix(
        control_offset_at(top_left),
        control_offset_at(top_left + 1),
        blend.x);
    vec2 bottom = mix(
        control_offset_at(top_left + 4),
        control_offset_at(top_left + 5),
        blend.x);
    VERTEX += mix(top, bottom, blend.y);
}

bool belongs_to_other_cell(vec2 seed, int index, float own_distance) {
    return index < seed_count
        && distance(capture_position, seed) + 0.001 < own_distance;
}

void fragment() {
    float own_distance = distance(capture_position, cell_seed);
    if (belongs_to_other_cell(seed_0, 0, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_1, 1, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_2, 2, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_3, 3, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_4, 4, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_5, 5, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_6, 6, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_7, 7, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_8, 8, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_9, 9, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_10, 10, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_11, 11, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_12, 12, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_13, 13, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_14, 14, own_distance)) { discard; }
    if (belongs_to_other_cell(seed_15, 15, own_distance)) { discard; }

    COLOR = texture(TEXTURE, UV) * COLOR;
}
