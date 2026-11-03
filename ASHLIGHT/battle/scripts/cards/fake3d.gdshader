shader_type canvas_item;

// 仅伪3D「透视形变」：把整张贴图当作刚性平面，按 x_rot/y_rot 做透视投影倾斜。
// 不含任何光效/阴影/抠色逻辑——纯几何扭曲。鼠标跟随 ±15° 即为卡牌倾斜效果。
uniform float fov : hint_range(1, 179) = 90.0;     // 相机视场角（越小透视越夸张）
uniform bool cull_back = true;                     // 背面剔除：旋转过半时隐藏背面，避免穿帮
uniform float y_rot : hint_range(-180, 180) = 0.0; // 绕Y轴（左右倾斜）
uniform float x_rot : hint_range(-180, 180) = 0.0; // 绕X轴（上下倾斜）
uniform float inset : hint_range(0, 1) = 0.0;      // 0=不缩放；>0=整体缩放留出旋转过扫边距，防裁边

varying flat vec2 o;
varying vec3 p;

// 顶点阶段：构造反向旋转矩阵，把 UV 平面投到相机空间
void vertex() {
	float sin_b = sin(y_rot / 180.0 * PI);
	float cos_b = cos(y_rot / 180.0 * PI);
	float sin_c = sin(x_rot / 180.0 * PI);
	float cos_c = cos(x_rot / 180.0 * PI);

	mat3 inv_rot_mat;
	inv_rot_mat[0][0] = cos_b;
	inv_rot_mat[0][1] = 0.0;
	inv_rot_mat[0][2] = -sin_b;

	inv_rot_mat[1][0] = sin_b * sin_c;
	inv_rot_mat[1][1] = cos_c;
	inv_rot_mat[1][2] = cos_b * sin_c;

	inv_rot_mat[2][0] = sin_b * cos_c;
	inv_rot_mat[2][1] = -sin_c;
	inv_rot_mat[2][2] = cos_b * cos_c;

	float t = tan(fov / 360.0 * PI);
	p = inv_rot_mat * vec3((UV - 0.5), 0.5 / t);
	float v = (0.5 / t) + 0.5;
	p.xy *= v * inv_rot_mat[2].z;
	o = v * inv_rot_mat[2].xy;

	VERTEX += (UV - 0.5) / TEXTURE_PIXEL_SIZE * t * (1.0 - inset);
}

void fragment() {
	if (cull_back && p.z <= 0.0) discard;

	// 把 3D 投影坐标反算回 UV 采样
	vec2 uv = (p.xy / p.z).xy - o;
	COLOR = texture(TEXTURE, uv + 0.5);
	// 仅在卡牌原始 [−0.5,0.5] 范围内绘制，避免越界采样/平铺
	COLOR.a *= step(max(abs(uv.x), abs(uv.y)), 0.5);
}
