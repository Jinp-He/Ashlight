shader_type canvas_item;
uniform vec4 silhouette_color : source_color = vec4(1.0);
varying float layer_alpha;
void vertex() { layer_alpha = COLOR.a; }
void fragment() {
    COLOR = vec4(silhouette_color.rgb, texture(TEXTURE, UV).a * layer_alpha);
}
