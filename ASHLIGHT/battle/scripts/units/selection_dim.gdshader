shader_type canvas_item;
uniform float top_darkness : hint_range(0.0, 1.0) = 0.2;
uniform float transparent_region : hint_range(0.0, 1.0) = 0.1;
uniform float full_dark_at : hint_range(0.01, 1.0) = 0.75;
uniform vec4 shade_color : source_color = vec4(0.13333333, 0.13333333, 0.13333333, 1.0);
uniform float art_top = 0.0;
uniform float art_bottom = 1.0;
uniform bool solid_black = false;
varying float layer_alpha;
void vertex() {
    layer_alpha = COLOR.a;
}
void fragment() {
    float height = clamp((UV.y - art_top) / max(art_bottom - art_top, 0.001), 0.0, 1.0);
    float ramp = clamp((height - transparent_region) / max(full_dark_at - transparent_region, 0.001), 0.0, 1.0);
    float darkness = solid_black ? 1.0 : mix(top_darkness, 1.0, ramp);
    vec3 shade = solid_black ? vec3(0.0) : shade_color.rgb;
    COLOR = vec4(shade, texture(TEXTURE, UV).a * layer_alpha * darkness);
}
