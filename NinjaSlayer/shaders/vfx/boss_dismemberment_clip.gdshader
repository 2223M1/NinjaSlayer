shader_type canvas_item;
render_mode unshaded;

uniform int seed_count = 1;
uniform int cell_index = 0;
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

varying vec2 body_local_position;

void vertex() {
    body_local_position = VERTEX;
}

bool belongs_to_other_cell(vec2 seed, int index, float own_distance) {
    return index != cell_index
        && distance(body_local_position, seed) + 0.001 < own_distance;
}

void fragment() {
    float own_distance = distance(body_local_position, cell_seed);
    if (seed_count > 0 && belongs_to_other_cell(seed_0, 0, own_distance)) { discard; }
    if (seed_count > 1 && belongs_to_other_cell(seed_1, 1, own_distance)) { discard; }
    if (seed_count > 2 && belongs_to_other_cell(seed_2, 2, own_distance)) { discard; }
    if (seed_count > 3 && belongs_to_other_cell(seed_3, 3, own_distance)) { discard; }
    if (seed_count > 4 && belongs_to_other_cell(seed_4, 4, own_distance)) { discard; }
    if (seed_count > 5 && belongs_to_other_cell(seed_5, 5, own_distance)) { discard; }
    if (seed_count > 6 && belongs_to_other_cell(seed_6, 6, own_distance)) { discard; }
    if (seed_count > 7 && belongs_to_other_cell(seed_7, 7, own_distance)) { discard; }
    if (seed_count > 8 && belongs_to_other_cell(seed_8, 8, own_distance)) { discard; }
}
