shader_type canvas_item;
uniform vec4 tint : source_color = vec4(1.0);
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float noise_strength : hint_range(0.0, 0.3) = 0.12;
varying float layer_alpha;
void vertex() { layer_alpha = COLOR.a; }
float hash21(vec2 point) {
    return fract(sin(dot(point, vec2(127.1, 311.7))) * 43758.5453);
}
void fragment() {
    vec4 source = texture(TEXTURE, UV);
    // 按实际像素距离从中心向外扩散，避免322x90画布把圆形拉扁。
    vec2 canvas_size = 1.0 / TEXTURE_PIXEL_SIZE;
    float radius = length((UV - vec2(0.5)) * canvas_size) / length(canvas_size * 0.5);
    float noise = hash21(floor(UV * canvas_size / 2.0));
    float threshold = radius + (noise - 0.5) * noise_strength;
    float remaining = smoothstep(progress - 0.025, progress + 0.025, threshold);
    if (progress <= 0.0) { remaining = 1.0; }
    if (progress >= 1.0) { remaining = 0.0; }
    // 只溶解旧图自身alpha；任何噪点都不能画到火焰轮廓之外。
    COLOR = vec4(source.rgb * tint.rgb, source.a * tint.a * layer_alpha * remaining);
}
