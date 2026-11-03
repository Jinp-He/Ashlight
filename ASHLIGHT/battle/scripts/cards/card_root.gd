@tool
class_name CardRoot
extends Node2D

## 卡牌视觉组件（纯视觉，数据由外部驱动）。
## 组成（自底向上层级）：card_base底图(居中) → card_frame边框(居中覆盖底图, 强化开关着色) → card_face卡面(可选, 夹在底图/边框之上、费用/稀有度/文字之下) → 稀有度组(左上, rarity_1/2/3) → 费用组(63x63旋转45°蓝/橙菱形方块+黑描边+费用数字, 纯代码绘制) → 文字组(名字/类型/效果/说明, X轴居中于卡牌) → 特殊特效帧动画(special_effect/*, 居中覆盖卡牌, 层级最底 z=-2, 渐变 acfcff→78c4ff, 平时不播, 触发时循环)。
## 强化开关: enhanced=true → 边框 9c660a；false → 5c6266。真实战斗逻辑后续接入，本组件仅暴露开关。
## 费用类型: cost_type=0 即时(蓝 00a1ff) / 1 执行(橙 ff8c33)，当前仅视觉；稀有度 rarity∈{1,2,3}。

const CARD_ART_DIR: String = "res://assets/cards/"
const FACE_DIR: String = "res://assets/cards/faces/"   # 角色专属卡面（所有角色共用一个目录，按 {role}{id}.png 命名（无下划线）：role∈{Zhouzhou,Irene,Rocket}，id=3位卡牌ID 如 001；例 Zhouzhou001.png）

## 颜色常量
const COLOR_ENHANCED: Color = Color(0.611, 0.4, 0.039, 1.0)   # 9c660a 强化边框
const COLOR_NORMAL: Color = Color(0.361, 0.384, 0.400, 1.0)   # 5c6266 非强化边框
const FLASH_UP_TIME: float = 0.2      # 连锁加强（调试）：边框闪白到全白时长（秒）
const FLASH_DOWN_TIME: float = 0.3    # 连锁加强（调试）：白边淡回原边框色时长（秒）
const COST_INSTANT: Color = Color(0.0, 0.631, 1.0, 1.0)       # 00a1ff 即时
const COST_EXECUTE: Color = Color(1.0, 0.549, 0.2, 1.0)       # ff8c33 执行
## 接牌颜色（菱形填充）：0 红 / 1 蓝 / 2 黄 / 3 绿，与 ChainRules.COLOR_NAMES 一致。
## 菱形内数字显示点数（抽到时确定）。
const COST_COLORS: Array[Color] = [
	Color(0.890, 0.231, 0.231, 1.0), # 0 红 e33b3b
	Color(0.0, 0.631, 1.0, 1.0),     # 1 蓝 00a1ff
	Color(0.957, 0.769, 0.098, 1.0), # 2 黄 f4c419
	Color(0.439, 0.671, 0.231, 1.0)  # 3 绿 70ab3b
]
const OUTLINE_BLACK: Color = Color(0.0, 0.0, 0.0, 1.0)
const COST_OUTLINE_SIZE: float = 3.6   # 黑边 = 4 * 0.9（费用组整体 90%）
const COST_SIZE: float = 63.0   # 费用方块边长（px，旋转 45° 后为菱形）= 70 * 0.9
const FONT_PATH: String = "res://assets/fonts/ashlight-cu.ttf"

## 文字字体与颜色常量
const FONT_NAME_PATH: String = "res://assets/fonts/DouyinSansBold.otf"   # 名字/效果/说明
const FONT_TYPE_PATH: String = "res://assets/fonts/Biantao.ttf"          # 类型
const TEXT_WHITE: Color = Color(1.0, 1.0, 1.0, 1.0)
const TEXT_OUTLINE: Color = Color(0.192, 0.224, 0.275, 1.0)   # 313946 名字描边
const TEXT_BLACK: Color = Color(0.0, 0.0, 0.0, 1.0)            # 效果
const TEXT_GREY: Color = Color(0.502, 0.502, 0.502, 1.0)       # 808080 说明
const CARD_TYPES: Array[String] = ["攻击", "状态", "防御"]

## 数值与正文同步缩放。
var _effect_font_size: int = 23

## 特殊特效动画（帧序列，平时不播放，触发特殊条件时循环播放；居中覆盖于卡牌、受卡牌整体 scale 影响）
const SPECIAL_EFFECT_DIR: String = "special_effect/"                       # 相对 CARD_ART_DIR
const SPECIAL_EFFECT_FRAMES: int = 6
const SPECIAL_EFFECT_FPS: float = 8.0
const SPECIAL_TOP_COLOR: Color = Color(0.674, 0.988, 1.0, 1.0)     # acfcff 渐变顶部
const SPECIAL_BOTTOM_COLOR: Color = Color(0.471, 0.769, 1.0, 1.0) # 78c4ff 渐变底部
const SPECIAL_GRADIENT_SHADER: String = """shader_type canvas_item;
uniform vec4 top_color : source_color = vec4(0.674, 0.988, 1.0, 1.0);
uniform vec4 bottom_color : source_color = vec4(0.471, 0.769, 1.0, 1.0);
void fragment() {
	float t = clamp(UV.y, 0.0, 1.0);
	vec4 grad = mix(top_color, bottom_color, t);
	vec4 tex = texture(TEXTURE, UV);
	COLOR = vec4(grad.rgb, tex.a);
}
"""

## 压暗（灰幕）着色器：单材质、双层叠加。
## 先向 dim_color 混 dim_amount，再向 dim2_color 混 dim2_amount（两层都沿 alpha 形状、不溢出）。
## 仅向目标灰阶混、不动色相/明度通道；透明区(圆角/镂空)因 tex.a=0 不绘制 → 天然不溢出。
## 激活(两层 amount 均=0)由调用方卸材质还原。dim_color 默认中灰(0.5)，dim2 默认黑(0.0)。
const DIM_SHADER: String = """shader_type canvas_item;
uniform vec3 dim_color : source_color = vec3(0.5);
uniform float dim_amount : hint_range(0.0, 1.0) = 0.0;
uniform vec3 dim2_color : source_color = vec3(0.0);
uniform float dim2_amount : hint_range(0.0, 1.0) = 0.0;
// 双层灰幕叠加：先向 dim_color 混 dim_amount，再向 dim2_color 混 dim2_amount。
// 每层都沿卡牌各部件 alpha 形状、圆角/透明处不溢出。
void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	vec3 c = tex.rgb * COLOR.rgb;
	c = mix(c, dim_color, dim_amount);
	c = mix(c, dim2_color, dim2_amount);
	COLOR = vec4(c, tex.a * COLOR.a);
}
"""

## 边框流光着色器（连锁加强特效）：以边框纹理(card_frame.png)的 alpha 作蒙版
## → 流光仅在边框形状内可见，透明(卡面)区绝不上色、绝不溢出到卡面。这就是「仅限边框大小」的落地方式。
## 流光沿极坐标角度「环游」卡框一周（atan2 近似周长参数）：一束高光带绕边框划过一圈（左→上→右→下→左）。
## render_mode blend_add：纯加法叠加到下方底色边框上（亮带处提亮，非亮带/mask 外不加任何颜色，不遮底色）。
## u_time 由 Tween 从 0→1 驱动（= 绕一圈）；u_width=亮带角宽；u_soft=边缘柔化；u_intensity=亮度；u_color=流光色。
const BORDER_FLOW_SHADER: String = """shader_type canvas_item;
render_mode blend_add;
uniform float u_time : hint_range(0.0, 4.0) = 0.0;
uniform vec3 u_color : source_color = vec3(1.0, 1.0, 1.0);
uniform float u_width : hint_range(0.01, 0.5) = 0.10;
uniform float u_soft : hint_range(0.0, 0.3) = 0.06;
uniform float u_intensity : hint_range(0.0, 3.0) = 1.2;
void fragment() {
	float mask = texture(TEXTURE, UV).a;
	vec2 c = UV - vec2(0.5, 0.5);
	float t = atan(c.y, c.x) / 6.2831853 + 0.5;
	float d = abs(t - fract(u_time));
	d = min(d, 1.0 - d);
	float band = smoothstep(u_width + u_soft, max(u_width - u_soft, 0.0), d);
	float glow = band * mask * u_intensity;
	COLOR = vec4(u_color * glow, glow);
}
"""
const FLOW_DURATION: float = 0.9   # 边框流光：绕卡框划过的总时长（秒）；实际转角由 FLOW_START_DEG + FLOW_END_DEG 决定
const FLOW_START_DELAY: float = 0.5   # 边框流光：加链环后延迟多久才开始（秒），避开旋转初期看不清的阶段
const FLOW_START_DEG: float = 40.0   # 边框流光：起始角偏移（度），把起始点往后推一点（非 0 起点）
const FLOW_END_DEG: float = 380.0   # 边框流光：旋转总角度（度），=380 即转过整圈再多 20°
const FLOW_FADE_DURATION: float = 0.8   # 边框流光：旋转过程中光晕淡出时长（秒，与旋转并行），由亮渐隐而非硬切
const FLOW_INTENSITY: float = 1.2   # 边框流光：峰值亮度（与着色器 u_intensity 初值一致）

## 白光斜扫着色器（连锁加强特效·与边框流光同时播放）：
## 以卡牌图(card_base.png)的 alpha 作蒙版 → 白光严格限制在卡牌图轮廓内、绝不溢出。
## 形状 = 一个从「卡图左下角稍外侧」向外扩散的「弧形环带」(annulus)：内半径 r_in 与外半径 r_out 一起增大，
## 白 = r_in < dist < r_out。于是：早期从左下角长出一小块白 → 中期弧形白波斜铺过整张卡 →
## 后期黑从左下角追回来、白色从右上角退出卡外。弧形边缘（不是直边），完全对上参考 GIF「卡牌闪光」。
## u_center=扩散圆心(UV)；u_r_in/u_r_out=内/外半径(以卡高为 1 的高度单位)；u_aspect=卡宽/卡高(保证正圆弧)；
## u_soft=弧边柔化；u_alpha=Tween 驱动的整体不透明度。普通 alpha 混合（白膜覆盖卡面）。
const CARD_FLASH_SHADER: String = """shader_type canvas_item;
uniform vec2 u_center = vec2(-0.35, 1.35);
uniform float u_r_in : hint_range(0.0, 3.0) = 0.0;
uniform float u_r_out : hint_range(0.0, 3.0) = 0.0;
uniform float u_soft : hint_range(0.0, 0.6) = 0.16;
uniform float u_aspect : hint_range(0.1, 3.0) = 0.694;
uniform vec4 u_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float u_alpha : hint_range(0.0, 1.0) = 0.0;
void fragment() {
	float mask = texture(TEXTURE, UV).a;                 // 卡牌图 alpha 蒙版 → 白光不溢出卡牌轮廓
	vec2 d = (UV - u_center) * vec2(u_aspect, 1.0);      // 换算到高度单位，保证弧边是正圆而非椭圆
	float dist = length(d);
	float outer = 1.0 - smoothstep(u_r_out - u_soft, u_r_out, dist);   // 1=在外半径内
	float inner = smoothstep(u_r_in, u_r_in + u_soft, dist);           // 0=在内半径内 → 乘起来=环带
	float band = outer * inner;
	float a = band * mask * u_alpha;
	COLOR = vec4(u_color.rgb, a);
}
"""
const FLASH_SWEEP_DELAY: float = 0.4      # 白光斜扫：触发后延迟多久开始（秒）；现由「旋转输入/开始」调用 → 延迟 0.4s 起播，与边框流光在时间上错开
const FLASH_SWEEP_DURATION: float = 0.9   # 白光斜扫：弧形白波从左下角扩散到右上角外的总时长（秒）
const FLASH_CENTER_UV: Vector2 = Vector2(-0.35, 1.35)   # 扩散圆心（卡图左下角稍外侧；弧边即以此为中心的正圆弧）
const FLASH_R_IN_MAX: float = 1.70        # 内半径峰值（高度单位；实际 r_in = t²×此值 → 早期近乎 0，从左下角先长出一块白）
const FLASH_R_OUT_MAX: float = 2.90       # 外半径峰值（高度单位；实际 r_out = t^EXP×此值 → 中期覆盖更大，第4/5帧白膜面积更宽）
const FLASH_R_OUT_EXP: float = 1.2        # 外半径增长指数（>1 → 早期外圈收得慢、中期铺得更开，配合 R_IN 让第4/5帧白膜面积明显增大）
const FLASH_SOFT: float = 0.14            # 弧形边缘柔化（高度单位）
const FLASH_MAX_ALPHA: float = 0.92       # 白光峰值不透明度（1.0=纯白覆盖卡面）

## 对外数据（由调用方设置；真实战斗数据接入后驱动这些值）
@export var card_face: Texture2D                       # 角色专属卡面（可选，未提供时仅显示底图模板）
@export var enhanced: bool = false                    # 强化开关 → 边框着色
@export var rarity: int = 1                            # 1/2/3 → 稀有度组显示对应图
@export var cost_type: int = 0                         # 0=即时 1=执行（玩法字段，菱形色不再由此驱动）
@export var cost_value: int = 1                        # 费用数字（当前随机生成，后续按表格填）
@export var cost_color: int = 0                        # 费用色(菱形填充+1+1匹配轴)：0蓝 1橙 2绿(70ab3b)
@export var card_name: String = "千里不留行"             # 卡牌名字（测试值，后续按卡牌数据填）
@export var card_type: int = 0                           # 0=攻击 1=状态 2=防御
@export var card_effect: String = "造成5点伤害。【迅捷】测试测试测试测试测试测试测试"   # 卡牌效果（测试值，词条/数字加粗；后缀为自动换行效果验证用）
@export var card_desc: String = "勤能补拙。"             # 卡牌说明（测试值）

## 布局微调（具体数值用户后续微调）
## ── 位置基准快照（2026-09-02 用户 F6 确认锁定，后续改版以此处为准）──
##   rarity_offset  = (2, 0)      稀有度组相对卡牌中心
##   cost_offset    = (10, 10)    费用组相对卡牌右上角
##   name_offset    = (0, 50)     名字（卡牌中心向下 50）
##   type_offset    = (0, 82)     类型（向下 82）
##   effect_offset  = (0, 132)    效果（向下 132，含随机后重居中基准）
##   desc_offset    = (0, 240)    说明（向下 240）
##   face_offset    = (0, -90)    卡面相对居中（向上 90）
##   wrap_inset     = 50          效果换行区左右内缩（仅控宽）
## 注：测试场景以代码 new() 创建 CardRoot，无任何 .tscn 实例覆盖 → 以上 export 默认即唯一基准来源。
@export_group("Layout")
@export var rarity_offset: Vector2 = Vector2(2.0, 0.0)   # 稀有度组相对卡牌中心偏移（左2，相对旧默认）
@export var cost_offset: Vector2 = Vector2(10.0, 10.0)    # 费用组相对卡牌右上角的偏移
@export var name_offset: Vector2 = Vector2(0.0, 50.0)      # 名字（向下 40 → 50）
@export var type_offset: Vector2 = Vector2(0.0, 82.0)      # 类型（原 80，向下 2 → 82）
@export var effect_offset: Vector2 = Vector2(0.0, 132.0)   # 效果（127，再向下 5 → 132）
@export var desc_offset: Vector2 = Vector2(0.0, 240.0)     # 说明（原 250，向上 10 → 240）
@export var face_offset: Vector2 = Vector2(0.0, -90.0)     # 卡面相对居中位置的偏移（累计 -70-60+30+10 = 向上 90px）
@export var wrap_inset: float = 50.0   # 效果换行区左右内缩量（左右各缩 wrap_inset，即你之前调出的红框尺寸；仅影响换行宽度，不含虚线框）

## 子节点
var _base: TextureRect
var _face: TextureRect
var _border: TextureRect
var _rarity_group: Node2D
var _rarity_imgs: Array[TextureRect] = []
var _cost_node: Node2D
var _cost_bg: ColorRect          # 黑色描边底板（比费用方块大 2*outline，旋转 45° 仅作描边）
var _cost_rect: ColorRect         # 费用方块填充（蓝/橙，70x70 旋转 45°，纯代码绘制）
var _cost_label: Label
var _text_group: Node2D
var _name_label: Label
var _type_label: Label
var _effect_label: RichTextLabel
var _desc_label: Label
var _special_anim: AnimatedSprite2D   # 特殊特效帧动画（平时不播放）
var _visual_group: Node2D            # 中性容器：所有可绘制子节点挂到此处（仅作父节点，不参与任何合成/着色）
var _font_name: Font
var _font_type: Font
var _flash_tween: Tween = null   # 连锁加强边框闪白 tween（调试；每次开头先 kill 防叠加）
var _border_glow: TextureRect        # 边框流光层（与 Border 同纹理/尺寸，用其 alpha 作蒙版 → 仅边框处显流光）
var _border_glow_mat: ShaderMaterial # 上述流光材质（u_time 由 Tween 从 0→1 驱动 = 绕卡框一圈）
var _flow_tween: Tween = null        # 边框流光 tween（每次开头先 kill 防叠加）
var _flash_overlay: TextureRect      # 白光斜扫层（与 Base 同纹理/同尺寸，用其 alpha 作蒙版 → 白光仅限卡牌图轮廓内）
var _flash_mat: ShaderMaterial       # 上述白光材质（u_r_in/u_r_out/u_alpha 由 Tween 驱动 = 弧形白波向左上扩散）
var _flash_sweep_tween: Tween = null # 白光斜扫 tween（每次开头先 kill 防叠加）

## 压暗（灰幕）状态：供 CardTest 对比 / 后续非激活手牌复用。
## 纯着色器处理，绝不写子节点颜色。0=原色；>0=沿卡牌形状叠灰幕(向中灰(0.5)混，如 0.5=50%灰)，透明/圆角处不溢出。
var _dim_level: float = 0.0
var _dim_material: ShaderMaterial = null
var _dim_targets: Array[CanvasItem] = []

## 资源引用
var _font: Font
var _card_w: float = 0.0   # 卡牌宽（_apply_layout 缓存，供 resized 信号重居中时使用）
var _face_files: Array[String] = []   # faces 目录下可用卡面文件名清单（_scan_face_files 填充）

func _ready() -> void:
	_build()
	_apply_all()
	_collect_dim_targets()   # 收集压暗叶子（set_dim 用；默认 0=原色不挂材质）

## 构建节点树（纯视觉节点，无逻辑耦合）
func _build() -> void:
	_font = load(FONT_PATH) as Font
	_font_name = load(FONT_NAME_PATH) as Font
	_font_type = load(FONT_TYPE_PATH) as Font

	# 统一视觉容器：所有可绘制子节点挂到此处（中性父节点，纯组织用途，不参与任何合成/着色）。
	_visual_group = Node2D.new()
	_visual_group.name = "VisualGroup"
	add_child(_visual_group)

	# 底图（居中，锚点=卡牌中心）
	_base = TextureRect.new()
	_base.name = "Base"
	_base.texture = _load("card_base.png")
	_base.expand_mode = TextureRect.ExpandMode.EXPAND_KEEP_SIZE
	_visual_group.add_child(_base)

	# 边框（居中，覆盖在底图上；着色由 enhanced 决定）
	_border = TextureRect.new()
	_border.name = "Border"
	_border.texture = _load("card_frame.png")
	_border.expand_mode = TextureRect.ExpandMode.EXPAND_KEEP_SIZE
	_visual_group.add_child(_border)

	# 边框流光层（连锁加强特效）：与 Border 同纹理/同尺寸/同位置，用 card_frame 的 alpha 作蒙版
	# → 流光只出现在边框形状内（卡面/中间透明区不上色 = 仅限边框大小）。
	# 平时不可见；play_border_flow() 时显示、由 Tween 驱动 u_time 绕边框划过一圈。
	# z_index=1 高于其它部件确保不被遮挡；因蒙版仅边框处 alpha>0，不会遮挡中央文字/费用。
	_border_glow = TextureRect.new()
	_border_glow.name = "BorderGlow"
	_border_glow.texture = _load("card_frame.png")
	_border_glow.expand_mode = TextureRect.ExpandMode.EXPAND_KEEP_SIZE
	_border_glow.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_border_glow.z_index = 1
	var glow_sh := Shader.new()
	glow_sh.code = BORDER_FLOW_SHADER
	_border_glow_mat = ShaderMaterial.new()
	_border_glow_mat.shader = glow_sh
	_border_glow_mat.set_shader_parameter("u_color", Color(1.0, 1.0, 1.0))
	_border_glow_mat.set_shader_parameter("u_width", 0.10)
	_border_glow_mat.set_shader_parameter("u_soft", 0.06)
	_border_glow_mat.set_shader_parameter("u_intensity", FLOW_INTENSITY)
	_border_glow_mat.set_shader_parameter("u_time", 0.0)
	_border_glow.material = _border_glow_mat
	_border_glow.visible = false
	_visual_group.add_child(_border_glow)

	# 卡面（可选）：层级夹在「底图/边框」之上、「费用/稀有度/文字」之下。
	# 实现方式：base/border/face 均 z_index=-1，按树序 base→border→face 创建，
	# 同 z 下后创建的 face 绘制在 border 之上；rarity/cost/text 在 face 之后创建，故在其之上。
	_face = TextureRect.new()
	_face.name = "Face"
	_face.texture = card_face
	_face.expand_mode = TextureRect.ExpandMode.EXPAND_KEEP_SIZE
	_face.visible = is_instance_valid(card_face)
	_visual_group.add_child(_face)

	# 白光斜扫层（连锁加强特效·与边框流光同时播放）：与 Base 同纹理/同尺寸/同位置，
	# 用 card_base 的 alpha 作蒙版 → 白光仅出现在卡牌图轮廓内（不溢出圆角/镂空）。
	# 层级：z_index=1，与边框流光(BorderGlow)同层（后创建→绘制在其上）；因此白光斜扫会盖在边框流光之上。
	# 稀有度组/费用组 z=2，绘制在更上层 → 不被白膜遮挡。文字(TextGroup)仍为默认 z=0，位于白膜之下。
	# 平时不可见；play_card_flash() 时显示、由 Tween 驱动 u_center 从「左下角外」平移到「右上角外」。
	_flash_overlay = TextureRect.new()
	_flash_overlay.name = "WhiteFlashSweep"
	_flash_overlay.texture = _load("card_base.png")
	_flash_overlay.expand_mode = TextureRect.ExpandMode.EXPAND_KEEP_SIZE
	_flash_overlay.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_flash_overlay.z_index = 1   # 白光斜扫层：z=1，与边框流光同层（后创建→绘制在其上）
	var flash_sh := Shader.new()
	flash_sh.code = CARD_FLASH_SHADER
	_flash_mat = ShaderMaterial.new()
	_flash_mat.shader = flash_sh
	_flash_mat.set_shader_parameter("u_color", Color(1.0, 1.0, 1.0, 1.0))
	_flash_mat.set_shader_parameter("u_center", FLASH_CENTER_UV)
	_flash_mat.set_shader_parameter("u_r_in", 0.0)
	_flash_mat.set_shader_parameter("u_r_out", 0.0)
	_flash_mat.set_shader_parameter("u_soft", FLASH_SOFT)
	_flash_mat.set_shader_parameter("u_alpha", 0.0)
	_flash_overlay.material = _flash_mat
	_flash_overlay.visible = false
	_visual_group.add_child(_flash_overlay)

	# 特殊特效动画（帧序列）：平时不播放、不可见；触发特殊条件时循环播放。
	# 居中覆盖于卡牌中心（centered=true, position=卡牌中心），受卡牌整体 scale 影响；位置不动。
	# 层级置于卡牌组最底层（z_index=-2，低于其它子节点的 -1），触发时作为底层发光，不遮挡文字/费用/边框。
	_special_anim = AnimatedSprite2D.new()
	_special_anim.name = "SpecialEffect"
	_special_anim.centered = true
	_special_anim.position = Vector2.ZERO
	var sf := SpriteFrames.new()
	for i in range(1, SPECIAL_EFFECT_FRAMES + 1):
		var tex := _load(SPECIAL_EFFECT_DIR + "special_effect_%02d.png" % i)
		if is_instance_valid(tex):
			sf.add_frame("default", tex)
	sf.set_animation_loop("default", true)
	sf.set_animation_speed("default", SPECIAL_EFFECT_FPS)
	_special_anim.sprite_frames = sf
	# 渐变着色（acfcff 顶 → 78c4ff 底），覆盖在帧动画上、保留帧的 alpha 轮廓
	var sh := Shader.new()
	sh.code = SPECIAL_GRADIENT_SHADER
	var mat := ShaderMaterial.new()
	mat.shader = sh
	mat.set_shader_parameter("top_color", SPECIAL_TOP_COLOR)
	mat.set_shader_parameter("bottom_color", SPECIAL_BOTTOM_COLOR)
	_special_anim.material = mat
	_special_anim.visible = false
	_visual_group.add_child(_special_anim)

	# 稀有度组（左上角，3 张图左下对齐堆叠，按 rarity 显示其一）
	_rarity_group = Node2D.new()
	_rarity_group.name = "RarityGroup"
	_rarity_group.z_index = 2   # 稀有度组：z=2（高于白光/边框流光，绘制在最上层）
	_visual_group.add_child(_rarity_group)
	for i in range(1, 4):
		var img := TextureRect.new()
		img.name = "Rarity%d" % i
		img.texture = _load("rarity_%d.png" % i)
		img.expand_mode = TextureRect.ExpandMode.EXPAND_KEEP_SIZE
		if is_instance_valid(img.texture):
			# 稀有度图为整卡尺寸透明图（图标绘于图内）→ 居中叠加，图标位置由图内坐标决定，后续用 rarity_offset 微调
			img.position = -img.texture.get_size() * 0.5
		_rarity_group.add_child(img)
		_rarity_imgs.append(img)

	# 费用组（黑描边底板 + 着色费用图 + 费用数字）
	_cost_node = Node2D.new()
	_cost_node.name = "CostNode"
	_cost_node.z_index = 2   # 费用组：z=2（高于白光/边框流光，绘制在最上层）
	_visual_group.add_child(_cost_node)

	_cost_bg = ColorRect.new()
	_cost_bg.name = "CostBg"
	_cost_bg.color = OUTLINE_BLACK
	_cost_bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_cost_node.add_child(_cost_bg)

	_cost_rect = ColorRect.new()
	_cost_rect.name = "CostRect"
	_cost_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_cost_node.add_child(_cost_rect)

	# 费用数字（ashlight-cu 字体，位于费用上方）
	_cost_label = Label.new()
	_cost_label.name = "CostLabel"
	_cost_label.text = str(cost_value)
	_cost_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_cost_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	var ls := LabelSettings.new()
	ls.font = _font
	ls.font_size = 54   # 60 * 0.9（费用组整体 90%）
	ls.font_color = Color(1.0, 1.0, 1.0, 1.0)
	ls.outline_size = 0
	ls.outline_color = OUTLINE_BLACK
	_cost_label.label_settings = ls
	_cost_node.add_child(_cost_label)

	# 文字组（名字/类型/效果/说明）：均为卡牌组子节点，受卡牌整体 scale 影响，X 轴居中于卡牌
	_text_group = Node2D.new()
	_text_group.name = "TextGroup"
	_visual_group.add_child(_text_group)

	# 名字：DouyinSansBold 24px 白 + 5px 313946 描边，居中
	_name_label = Label.new()
	_name_label.name = "NameLabel"
	_name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	var ls_name := LabelSettings.new()
	ls_name.font = _font_name
	ls_name.font_size = 35
	ls_name.font_color = TEXT_WHITE
	ls_name.outline_size = 7
	ls_name.outline_color = TEXT_OUTLINE
	_name_label.label_settings = ls_name
	_text_group.add_child(_name_label)

	# 类型：biantao 18px 白，居中
	_type_label = Label.new()
	_type_label.name = "TypeLabel"
	_type_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_type_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	var ls_type := LabelSettings.new()
	ls_type.font = _font_type
	ls_type.font_size = 22
	ls_type.font_color = TEXT_WHITE
	_type_label.label_settings = ls_type
	_text_group.add_child(_type_label)

	# 效果：RichTextLabel，DouyinSansBold 黑，词条/数字加粗，居中
	# 自动换行，宽度锁定；高度按 get_content_height 显式同步，避免重排残留旧高度。
	_effect_label = RichTextLabel.new()
	_effect_label.name = "EffectLabel"
	_effect_label.bbcode_enabled = true
	_effect_label.fit_content = false
	_effect_label.scroll_active = false
	_effect_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_effect_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_effect_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_effect_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# Inspector 会继承编辑器的深色 RichTextLabel 底板；始终透出卡牌自带的浅色文字区。
	_effect_label.add_theme_stylebox_override("normal", StyleBoxEmpty.new())
	_effect_label.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	_effect_label.add_theme_color_override("default_color", TEXT_BLACK)   # 默认黑；关键数值仅换字体/放大、不再上色（词条/目标词均黑，见 _format_effect）
	_effect_label.add_theme_font_override("default_font", _font_name)
	_effect_label.add_theme_font_override("bold_font", _font_name)   # 加粗用同族字体（合成粗体），避免回退到其它字体变样
	_effect_label.add_theme_font_size_override("normal_font_size", 23)
	_effect_label.add_theme_font_size_override("bold_font_size", 23) # 关键：不加这句，[b] 内容会回退到默认 bold 字号（更小）
	_effect_label.resized.connect(_on_effect_resized)   # 引擎刷新换行高度(size.y)后自动重居中，消除异步导致的偏移
	_text_group.add_child(_effect_label)

	# 说明：DouyinSansBold 15px 808080，居中
	_desc_label = Label.new()
	_desc_label.name = "DescLabel"
	_desc_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_desc_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	var ls_desc := LabelSettings.new()
	ls_desc.font = _font_name
	ls_desc.font_size = 15
	ls_desc.font_color = TEXT_GREY
	_desc_label.label_settings = ls_desc
	_text_group.add_child(_desc_label)

	# 子节点保持默认 z（同层按树序叠放）；特殊特效保留 z_index=-2 作独立底层辉光（默认不可见）。
	_special_anim.z_index = -2   # 层级置底：位于卡牌组最底层（不遮挡文字/费用/边框）

	_scan_face_files()   # 缓存 faces 目录可用卡面清单（供 randomize_face / set_face_by_file 使用）

## 应用全部状态
func _apply_all() -> void:
	_apply_enhanced()
	_apply_rarity()
	_apply_cost()
	_apply_text()
	_apply_layout()

## 连锁升级/取消时实时改卡面效果文字（供 HandManager 调用）。
## 直接改写 card_effect 并重建 bbcode 上色 + 重居中；由于伪3D 快照是包含本 CardRoot 的实时 SubViewport，
## 改此处文字即实时刷新快照纹理，无需重建视口（实现「丝滑替换不卡壳」）。
func set_effect_text(s: String) -> void:
	card_effect = s
	if is_instance_valid(_effect_label):
		_render_effect_text()
	_on_effect_resized()   # 文字变化后按最新 size.y 重居中，消除异步偏移


## 连锁加强边框闪白（调试效果）：边框在 FLASH_UP_TIME 内从原色闪到纯白，再在 FLASH_DOWN_TIME 内淡回原边框色。
## 与伪3D 旋转完全并行：_border 是 CardRoot 子节点、在 SubViewport 内实时渲染 → 旋转中也能看到闪。
## 原边框色由 enhanced 决定（强化 9c660a / 普通 5c6266，见 _apply_enhanced），闪完精确回到该色。
func flash_border_boost() -> void:
	if not is_instance_valid(_border):
		return
	var base_color: Color = COLOR_ENHANCED if enhanced else COLOR_NORMAL
	if is_instance_valid(_flash_tween):
		_flash_tween.kill()
	_flash_tween = create_tween()
	_flash_tween.tween_property(_border, "modulate", Color.WHITE, FLASH_UP_TIME).set_ease(Tween.EASE_OUT)
	_flash_tween.tween_property(_border, "modulate", base_color, FLASH_DOWN_TIME).set_ease(Tween.EASE_IN)


## 边框流光（连锁加强特效）：一束高光带沿边框环游一圈。
## 仅限边框：着色器用 card_frame 的 alpha 作蒙版 → 流光只出现在边框形状内，绝不溢出到卡面。
## 与伪3D 旋转完全并行：_border_glow 是 CardRoot 子节点、在 SubViewport 内实时渲染 → 旋转中也能看到流光划过。
## 实现：显示流光层 → Tween 把 u_time 从 0 驱动到 1（=绕卡框一圈）→ 结束隐藏并复位（下次从头开始）。
## 调参：方向/带宽改 u_width、柔化改 u_soft、亮度改 u_intensity、颜色改 u_color、时长改 FLOW_DURATION。
func play_border_flow() -> void:
	if not is_instance_valid(_border_glow) or _border_glow_mat == null:
		return
	if is_instance_valid(_flow_tween):
		_flow_tween.kill()
	_set_flow_time(0.0)
	_border_glow.visible = false   # 延迟期间保持隐藏，避免与旋转初期重叠、看不清
	_flow_tween = create_tween()
	_flow_tween.tween_interval(FLOW_START_DELAY)   # 先等 0.5s（卡牌转过初始盲段）再开始
	_flow_tween.tween_callback(func() -> void:
		if is_instance_valid(_border_glow):
			_border_glow.visible = true
	)
	# 旋转：从起始角偏移 FLOW_START_DEG 起、再转过 FLOW_END_DEG(380°) 总行程，角速度逐渐慢下来（缓出）。
	_flow_tween.tween_method(_set_flow_time, FLOW_START_DEG / 360.0, (FLOW_START_DEG + FLOW_END_DEG) / 360.0, FLOW_DURATION).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	# 淡出与旋转并行（.parallel）：旋转进行中亮度即从峰值渐降到 0 → 边转边淡，不占用额外时间。
	# EASE_IN 让亮度前段保持、后段才快速下落，整圈扫过期间均可看见流光。
	_flow_tween.parallel().tween_method(_set_flow_intensity, FLOW_INTENSITY, 0.0, FLOW_FADE_DURATION).set_ease(Tween.EASE_IN).set_trans(Tween.TRANS_SINE)
	_flow_tween.finished.connect(_on_flow_finished)

## 驱动流光相位（0→1 = 绕卡框一周；着色器内 fract 使其首尾相接不跳变）。
func _set_flow_time(v: float) -> void:
	if _border_glow_mat != null:
		_border_glow_mat.set_shader_parameter("u_time", v)

## 驱动流光亮度（淡出段用：从峰值降到 0 → 光晕渐隐而非硬切）。
func _set_flow_intensity(v: float) -> void:
	if _border_glow_mat != null:
		_border_glow_mat.set_shader_parameter("u_intensity", v)

## 流光播完（含淡出）：复位亮度与相位、隐藏，供下次从头播放。
func _on_flow_finished() -> void:
	_set_flow_intensity(FLOW_INTENSITY)
	_set_flow_time(0.0)
	if is_instance_valid(_border_glow):
		_border_glow.visible = false


## 白光斜扫（连锁加强特效）：一道弧形白波从卡图「左下角稍外侧」向左上扩散，
## 斜铺过整张卡后从右上角退出卡外（参考 GIF「卡牌闪光」的形状：早期左下角一小块白 → 中期弧形白波铺满 →
## 后期黑从左下角追回、白色从右上角退出）。本质是「环形带」：内半径 r_in 与外半径 r_out 一起增大，白 = r_in<dist<r_out。
## 仅限卡牌图：着色器用 card_base 的 alpha 作蒙版 → 白光只出现在卡牌图轮廓内，绝不溢出。
## 与伪3D 旋转完全并行：_flash_overlay 是 CardRoot 子节点、在 SubViewport 内实时渲染 → 旋转中也能看到白光斜扫。
## 触发时机（2026-09-26 调整）：不再与边框流光同步播放，改由 HandManager 在「旋转输入/开始(_combo_spin_refresh)」时调用 →
##   即每次 +1 刷新旋转冲量那一刻起播、延迟 FLASH_SWEEP_DELAY 秒，与边框流光（加链环即延迟播放）在时间上错开。
## 实现：先等 FLASH_SWEEP_DELAY → 显示 → Tween 把进度 0→1（驱动 r_in/r_out）→ 结束隐藏复位。
## 调参：形状由 _set_flash_progress 驱动；扩散圆心改 FLASH_CENTER_UV、白波宽度改 FLASH_R_IN_MAX/FLASH_R_OUT_MAX/
##       FLASH_R_OUT_EXP、边缘柔化改 FLASH_SOFT、纯白/半透明白改 FLASH_MAX_ALPHA、时长改 FLASH_SWEEP_DURATION、延迟改 FLASH_SWEEP_DELAY。
func play_card_flash() -> void:
	if not is_instance_valid(_flash_overlay) or _flash_mat == null:
		return
	if is_instance_valid(_flash_sweep_tween):
		_flash_sweep_tween.kill()
	_set_flash_progress(0.0)
	_flash_mat.set_shader_parameter("u_alpha", 0.0)
	_flash_overlay.visible = false
	_flash_sweep_tween = create_tween()
	_flash_sweep_tween.tween_interval(FLASH_SWEEP_DELAY)   # 先等 0.4s（旋转输入后延迟起播，与边框流光错开）
	_flash_sweep_tween.tween_callback(func() -> void:
		if is_instance_valid(_flash_overlay):
			_flash_overlay.visible = true
	)
	# 弧形白波从「左下角外」向左上扩散：弧边滑入 → 斜铺过卡面 → 从右上角滑出，退出卡外。
	_flash_sweep_tween.tween_method(_set_flash_progress, 0.0, 1.0, FLASH_SWEEP_DURATION).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_SINE)
	_flash_sweep_tween.finished.connect(_on_flash_finished)

## 驱动白光斜扫进度 t∈[0,1]：内半径 r_in=t²×峰值（早期近乎 0 → 先从左下角长出一块白），
## 外半径 r_out=t^EXP×峰值（EXP>1 → 早期外圈收得慢、中期铺得更开，第4/5帧白膜面积明显更大）；
## 两者共同形成从下左向右上扩散、中段覆盖更宽的弧形白波。透明度做两端渐隐包络。
## 参数经参考 GIF 逐帧覆盖率拟合（第4帧≈0.79、第5帧≈0.91，较旧版 0.68 明显更大，又不至全白糊掉）。
func _set_flash_progress(t: float) -> void:
	if _flash_mat == null:
		return
	_flash_mat.set_shader_parameter("u_r_in", t * t * FLASH_R_IN_MAX)
	_flash_mat.set_shader_parameter("u_r_out", pow(t, FLASH_R_OUT_EXP) * FLASH_R_OUT_MAX)
	var a: float = FLASH_MAX_ALPHA * smoothstep(0.0, 0.10, t) * (1.0 - smoothstep(0.90, 1.0, t))
	_flash_mat.set_shader_parameter("u_alpha", a)

## 白光斜扫播完：复位不透明度与圆心、隐藏，供下次从头播放。
func _on_flash_finished() -> void:
	if _flash_mat != null:
		_flash_mat.set_shader_parameter("u_alpha", 0.0)
		_flash_mat.set_shader_parameter("u_r_in", 0.0)
		_flash_mat.set_shader_parameter("u_r_out", 0.0)
	if is_instance_valid(_flash_overlay):
		_flash_overlay.visible = false


## 随机生成一组合法的卡牌视觉组合（测试场景「随机」按钮调用）。
## 真实战斗数据接入后，此随机逻辑应被外部数据驱动替换。
func randomize_combo() -> void:
	enhanced = randi() % 2 == 0
	rarity = randi_range(1, 3)
	cost_type = randi() % 2
	cost_value = randi_range(1, 9)
	card_type = randi_range(0, 2)
	randomize_face()   # 随机取一份卡面
	cost_color = randi() % COST_COLORS.size()   # 四色随机（测试用）
	_apply_all()

## 扫描 faces 目录，缓存可用卡面文件名清单（仅取 .png，不含子目录）。
## 命名约定见 FACE_DIR 常量注释：{role}_{id}.png。
func _scan_face_files() -> void:
	_face_files.clear()
	var dir := DirAccess.open(FACE_DIR)
	if dir == null:
		push_warning("CardRoot: 无法打开卡面目录: %s" % FACE_DIR)
		return
	dir.list_dir_begin()
	var fname := dir.get_next()
	while fname != "":
		if not dir.current_is_dir() and fname.get_extension().to_lower() == "png":
			_face_files.append(fname)
		fname = dir.get_next()
	dir.list_dir_end()

## 按 faces 目录下的文件名设置卡面（file_name 如 "Zhouzhou_001.png"）。
## 同时更新 card_face 导出值与 _face 纹理；纹理缺失仅告警并隐藏卡面，不崩。
func set_face_by_file(file_name: String) -> void:
	var path := FACE_DIR + file_name
	if not ResourceLoader.exists(path):
		push_warning("CardRoot: 卡面未导入，跳过: %s" % path)
		return
	card_face = load(path) as Texture2D
	if is_instance_valid(_face):
		_face.texture = card_face
		_face.visible = is_instance_valid(card_face)

## 随机选取一份卡面（测试/随机按钮用）。清单为空时静默返回。
func randomize_face() -> void:
	if _face_files.is_empty():
		return
	var f := _face_files[randi_range(0, _face_files.size() - 1)]
	set_face_by_file(f)

## 按角色（如 "Zhouzhou"/"Irene"/"Rocket"）随机取一份该角色卡面。
## 手牌系统调用，保证卡面只来自 battle 已有角色（周周/艾琳/火炮），不混其它来源。
## 实际命名 {role}{id}.png（无下划线，如 Zhouzhou001.png）：过滤「以 role 开头且其后为纯数字」的文件。
## 清单为空时回退到全盘扫描一次；仍为空则告警返回。
func random_face_for_role(role: String) -> void:
	if _face_files.is_empty():
		_scan_face_files()
	var pool: Array[String] = []
	for f in _face_files:
		var rest := f.trim_prefix(role)
		if f.begins_with(role) and rest.get_basename().is_valid_int():
			pool.append(f)
	if pool.is_empty():
		push_warning("CardRoot: 无角色 %s 的卡面，跳过" % role)
		return
	set_face_by_file(pool[randi_range(0, pool.size() - 1)])

## 激活特殊特效动画（平时不播放；触发特殊条件时调用，循环播放）。
## 作为 CardRoot 子节点，自动继承卡牌整体 scale 与对齐。
func play_special_effect() -> void:
	if is_instance_valid(_special_anim):
		_special_anim.visible = true
		if not _special_anim.is_playing():
			_special_anim.play()

## 停止特殊特效动画（恢复为平时不播放、不可见状态）。
func stop_special_effect() -> void:
	if is_instance_valid(_special_anim):
		_special_anim.stop()
		_special_anim.visible = false

## 切换特殊特效播放状态，返回切换后的「是否正在播放」。
func toggle_special_effect() -> bool:
	if not is_instance_valid(_special_anim):
		return false
	if _special_anim.visible and _special_anim.is_playing():
		stop_special_effect()
		return false
	play_special_effect()
	return true

## 强化开关 → 边框着色
func _apply_enhanced() -> void:
	if is_instance_valid(_border) and is_instance_valid(_border.texture):
		_border.modulate = COLOR_ENHANCED if enhanced else COLOR_NORMAL

## 稀有度 → 显示对应图（组内左下角对齐已由 _build 固定）
func _apply_rarity() -> void:
	for i in _rarity_imgs.size():
		var img := _rarity_imgs[i] as TextureRect
		img.visible = (i + 1) == rarity

## 费用 → 蓝/橙填充方块（70x70 旋转 45° 菱形，纯代码绘制）+ 黑描边底板 + 数字
## 方块与黑边同为旋转 45° 的同心正方形：黑边比填充大 2*outline，故仅在菱形四周露黑边。
func _apply_cost() -> void:
	if is_instance_valid(_cost_label):
		_cost_label.text = str(cost_value)
	if is_instance_valid(_cost_rect):
		var fg := Vector2(COST_SIZE, COST_SIZE)
		var bg := fg + Vector2(COST_OUTLINE_SIZE * 2.0, COST_OUTLINE_SIZE * 2.0)
		_cost_rect.color = _cost_color()
		_cost_rect.size = fg
		_cost_rect.position = -fg * 0.5
		_cost_rect.pivot_offset = fg * 0.5   # 绕自身中心旋转，否则描边与填充不同心
		_cost_rect.rotation = PI * 0.25
		_cost_bg.size = bg
		_cost_bg.position = -bg * 0.5
		_cost_bg.pivot_offset = bg * 0.5     # 同上：黑边底板也绕中心转，保证与填充同心、黑边均匀
		_cost_bg.rotation = PI * 0.25

## 布局：底图/边框/卡面居中；稀有度组左上；费用组右上
func _apply_layout() -> void:
	var base_size := _base.texture.get_size() if is_instance_valid(_base.texture) else Vector2(256.0, 384.0)
	var bw := base_size.x
	var bh := base_size.y
	_card_w = bw   # 缓存卡牌宽，供 _on_effect_resized 重居中时使用
	_base.position = -base_size * 0.5
	if is_instance_valid(_face.texture):
		_face.position = -_face.texture.get_size() * 0.5 + face_offset
	_border.position = -_border.texture.get_size() * 0.5 if is_instance_valid(_border.texture) else -base_size * 0.5
	# 流光层与边框完全重合（同纹理同尺寸 → 蒙版与边框像素对齐）
	if is_instance_valid(_border_glow):
		_border_glow.position = -_border_glow.texture.get_size() * 0.5 if is_instance_valid(_border_glow.texture) else -base_size * 0.5

	# 白光斜扫层：与底图完全重合（同纹理同尺寸 → 蒙版与卡牌轮廓像素对齐）；
	# 并把「卡宽/卡高」传给着色器，使 UV 差换算为真实像素比例 → 白光是正圆而非椭圆。
	if is_instance_valid(_flash_overlay):
		_flash_overlay.position = -_flash_overlay.texture.get_size() * 0.5 if is_instance_valid(_flash_overlay.texture) else -base_size * 0.5
		if _flash_mat != null and bh > 0.0:
			_flash_mat.set_shader_parameter("u_aspect", bw / bh)

	# 稀有度图与卡牌同尺寸、居中叠加；组偏移直接相对卡牌中心微调图标落点（默认极小偏移，用户按需改 rarity_offset）
	_rarity_group.position = rarity_offset

	# 费用组：卡牌右上角 - 偏移，整体再向左 325 / 向下 60
	_cost_node.position = Vector2(bw * 0.5, -bh * 0.5) + Vector2(-cost_offset.x - 325.0, cost_offset.y + 60.0)
	if is_instance_valid(_cost_label):
		# 费用数字居中于费用菱形（文字与美术居中对齐）：Label 框中心落在菱形中心 (0,0)
		_cost_label.size = Vector2(90.0, 63.0)   # 100x70 * 0.9（费用组整体 90%）
		_cost_label.position = Vector2(-_cost_label.size.x * 0.5 + 6.0, -_cost_label.size.y * 0.5)   # 数字右移 6px（2+4）

	# 文字组：四段文字 X 轴居中于卡牌，Y 由各自偏移决定（相对卡牌中心）
	_position_text(_name_label, name_offset, bw, Vector2(bw, 44.0))
	_position_text(_type_label, type_offset, bw, Vector2(bw, 28.0))
	_position_effect(effect_offset, bw)
	_position_text(_desc_label, desc_offset, bw, Vector2(bw, 20.0))

## 沙漏恒用美术预描边版 AttackRange.png（描边已烘进图内），不随打击面切换；
## 数字：n<0 → A(全场)；n==0 → 0(自身/当前格)；否则 N(前向 N 格)。
## 打击面编码（+ 后缀）：scope=群体 时数字后追加 "+"（含全场 ⏳A+）；单体不追加。


## 当前卡牌目标规则存放于 CardEffect。

## 文字：名字/类型/效果/说明（效果中词条与数字加粗）
func _apply_text() -> void:
	if is_instance_valid(_name_label):
		_name_label.text = card_name
	if is_instance_valid(_type_label):
		var idx := clampi(card_type, 0, CARD_TYPES.size() - 1)
		_type_label.text = CARD_TYPES[idx]
	if is_instance_valid(_effect_label):
		_render_effect_text()
	if is_instance_valid(_desc_label):
		_desc_label.text = card_desc

## 效果文本（bbcode 上色 + 关键数值换字体/放大 + 左右留空位）：
##  · 关键数值（含 ▲/▼）：扁桃体字体(Biantao.ttf) + 放大字号 + 黑色（与正文同色，不上金）；
##    ▲=增益/连锁提升→红(#e22600)，▼=被削减→绿(#5cc600)（取伤害数字色库），由 CardData._arrow_for 比较显示值 vs 基础值得出。
##  · 【词条】(伤害/护盾/血量/士气/出血 等)：保留【…】，黑色（默认色）。
##  · 目标词(我方/敌方/单体/群体/全部/全场)与说明文字：默认黑、不另上色、不带【】。
func _format_effect(src: String) -> String:
	var out := ""
	var re := RegEx.new()
	re.compile("【([^】]*)】")   # 仅剩 词条的【…】
	var pos := 0
	for m in re.search_all(src):
		var s: int = m.get_start()
		var e: int = m.get_end()
		out += _format_plain(src.substr(pos, s - pos))   # 块间纯文本：处理关键数值
		out += "【%s】" % m.get_string(1)                 # 词条：黑色、保留【】
		pos = e
	out += _format_plain(src.substr(pos))
	return out

## 块间纯文本：仅处理关键数值（含 ▲/▼）→ 扁桃体 + 放大 + 黑 + 左右留空位；
## 其余（含目标词、说明文字）保持默认黑、不另上色、不带【】。
func _format_plain(text: String) -> String:
	var re_num := RegEx.new()
	re_num.compile("(\\d+[▲▼]?)")
	var out := ""
	var pos := 0
	for m in re_num.search_all(text):
		var s: int = m.get_start()
		var e: int = m.get_end()
		out += text.substr(pos, s - pos)
		out += " " + _format_key_number(m.get_string(0)) + " "   # 左右留空位
		pos = e
	out += text.substr(pos)
	return out

## 关键数值（数字 + 可选 ▲/▼）：扁桃体字体 + 放大字号 + 黑（与正文同色，不上金）；
## 箭头用抖音体同字号，▲=上升红(#e22600) ▼=下降绿(#5cc600)。字体/字号/箭头着色统一交由
## EffectTextFormat.key_number（与意图展开组同一套格式规格，杜绝漂移）。
func _format_key_number(s: String) -> String:
	var re_num := RegEx.new()
	re_num.compile("^(\\d+)([▲▼]?)$")
	var mm := re_num.search(s)
	var num: String = s
	var arrow: String = ""
	if mm != null:
		num = mm.get_string(1)
		arrow = mm.get_string(2)
	return EffectTextFormat.key_number(num, arrow, _effect_font_size + 5)

## 将文字控件按「卡牌宽居中 + 偏移」定位（X 居中于卡牌，Y 居中于偏移点）
func _position_text(ctrl: Control, offset: Vector2, card_w: float, size: Vector2) -> void:
	if not is_instance_valid(ctrl):
		return
	ctrl.size = size
	ctrl.position = Vector2(-card_w * 0.5 + offset.x, offset.y - size.y * 0.5)

## 效果区锁定宽度，内容变更后同步实际高度。多效果使用较宽的文字区域并自适应字号。
func _position_effect(offset: Vector2, card_w: float) -> void:
	if not is_instance_valid(_effect_label):
		return
	var w := card_w - 2.0 * _effect_inset()
	_effect_label.custom_minimum_size = Vector2(w, 0.0)
	_effect_label.size.x = w
	# 先用当前 size.y 给一个近似位置；精确居中由 _on_effect_resized 在尺寸刷新后完成。
	_effect_label.position = Vector2(-w * 0.5 + offset.x, _effect_center_y() - _effect_label.size.y * 0.5)

## 引擎刷新效果文字换行高度(size.y)后回调：用最新尺寸重新垂直居中（消除异步导致的偏移）。
## 连接于 _build（_effect_label.resized）；触发条件：初始布局 / 改文字 / 窗口/换行重算等 size 变化时。
func _on_effect_resized() -> void:
	if not is_instance_valid(_effect_label):
		return
	var multi: bool = card_effect.contains("\n")
	var available: float = desc_offset.y - type_offset.y - 38.0
	if multi and _effect_label.get_content_height() > available and _effect_font_size > 12:
		_effect_font_size -= 1
		_update_effect_fonts()
	var w := _card_w - 2.0 * _effect_inset()
	_effect_label.size.y = maxf(1.0, float(_effect_label.get_content_height()))
	_effect_label.position = Vector2(-w * 0.5 + effect_offset.x, _effect_center_y() - _effect_label.size.y * 0.5)

func _effect_inset() -> float:
	return 30.0 if card_effect.contains("\n") else wrap_inset

func _effect_center_y() -> float:
	return (type_offset.y + 24.0 + desc_offset.y - 14.0) * 0.5 if card_effect.contains("\n") else effect_offset.y

func _render_effect_text() -> void:
	_effect_font_size = 18 if card_effect.contains("\n") else 23
	_update_effect_fonts()

func _update_effect_fonts() -> void:
	_effect_label.add_theme_font_size_override("normal_font_size", _effect_font_size)
	_effect_label.add_theme_font_size_override("bold_font_size", _effect_font_size)
	_effect_label.text = _format_effect(card_effect)
	_on_effect_resized.call_deferred()

## 费用着色：由 cost_color 索引 COST_COLORS（蓝/橙/绿），越界回退蓝。
func _cost_color() -> Color:
	return COST_COLORS[clampi(cost_color, 0, COST_COLORS.size() - 1)]

## 安全加载纹理：未导入/缺失仅告警返回 null，不崩
func _load(file_name: String) -> Texture2D:
	var path := CARD_ART_DIR + file_name
	if not ResourceLoader.exists(path):
		push_warning("CardRoot: 卡牌美术未导入，跳过（请先在编辑器导入）: %s" % path)
		return null
	return load(path) as Texture2D

## 返回选中描边用的源纹理（Border 帧纹理优先，回退 Base 底图），供 HandCard 沿真实卡牌边缘描边。
## 不返回卡面/文字等内部节点——仅取定义卡牌外缘轮廓的纹理。
func get_outline_source_texture() -> Texture2D:
	if is_instance_valid(_border) and is_instance_valid(_border.texture):
		return _border.texture
	if is_instance_valid(_base) and is_instance_valid(_base.texture):
		return _base.texture
	return null

## ── 压暗（灰幕）控制（CardTest 对比 / 非激活手牌复用）──
## 纯着色器处理，绝不写任何子节点颜色属性：0=原色(卸材质)=100% 还原基础原色。
## 逐叶子挂共享灰幕着色器（跳过 _special_anim），每个部件仅在自身 alpha 形状内向中灰混、圆角/透明处不溢出。

## 非激活卡牌状态（CardTest 选定，2026-09-05）：0.3暗灰 50% + 0黑 40%。
## 费用组（菱形 _cost_rect + 数字 _cost_label + 描边底板 _cost_bg）始终不参与压暗，见 _collect_dim_targets。
const INACTIVE_DIM_COLOR1: Color = Color(0.3, 0.3, 0.3)
const INACTIVE_DIM_AMOUNT1: float = 0.5
const INACTIVE_DIM_COLOR2: Color = Color(0.0, 0.0, 0.0)
const INACTIVE_DIM_AMOUNT2: float = 0.4

## 套用「未激活」状态：整体 0.3暗灰 50% + 0黑 40%，费用组保持原色高亮。
func set_inactive() -> void:
	set_dim_layers(INACTIVE_DIM_COLOR1, INACTIVE_DIM_AMOUNT1, INACTIVE_DIM_COLOR2, INACTIVE_DIM_AMOUNT2)

## 设置单层压暗（默认向中灰(0.5)混）。amount<=0 → 原色（卸材质）；amount>0 → 沿卡牌形状叠灰幕。
func set_dim(amount: float, color: Color = Color(0.5, 0.5, 0.5)) -> void:
	_dim_level = clampf(amount, 0.0, 1.0)
	if _dim_level <= 0.0:
		_detach_dim()
		return
	_ensure_dim_material()
	_dim_material.set_shader_parameter("dim_color", color)
	_dim_material.set_shader_parameter("dim_amount", _dim_level)
	_dim_material.set_shader_parameter("dim2_amount", 0.0)
	_attach_dim()

## 设置双层压暗叠加：先向 color1 混 amount1，再向 color2 混 amount2。两层皆<=0 → 原色（卸材质）。
## 每层都沿卡牌 alpha 形状、圆角/透明处不溢出。
func set_dim_layers(color1: Color, amount1: float, color2: Color, amount2: float) -> void:
	_dim_level = maxf(clampf(amount1, 0.0, 1.0), clampf(amount2, 0.0, 1.0))
	if _dim_level <= 0.0:
		_detach_dim()
		return
	_ensure_dim_material()
	_dim_material.set_shader_parameter("dim_color", color1)
	_dim_material.set_shader_parameter("dim_amount", clampf(amount1, 0.0, 1.0))
	_dim_material.set_shader_parameter("dim2_color", color2)
	_dim_material.set_shader_parameter("dim2_amount", clampf(amount2, 0.0, 1.0))
	_attach_dim()

## 从另一张卡复制全部对外数据并重新应用（供 CardTest 同步对比：4 张卡展示同一组合）。
func copy_state_from(src: CardRoot) -> void:
	enhanced = src.enhanced
	rarity = src.rarity
	cost_type = src.cost_type
	cost_value = src.cost_value
	card_type = src.card_type
	card_name = src.card_name
	card_effect = src.card_effect
	card_desc = src.card_desc
	card_face = src.card_face
	if is_instance_valid(_face):
		_face.texture = card_face
		_face.visible = is_instance_valid(card_face)
	_apply_all()

## 收集需压暗的叶子节点（跳过 _special_anim 与容器 _visual_group）。
func _collect_dim_targets() -> void:
	_dim_targets.clear()
	if is_instance_valid(_base): _dim_targets.append(_base)
	if is_instance_valid(_border): _dim_targets.append(_border)
	if is_instance_valid(_face): _dim_targets.append(_face)
	for img in _rarity_imgs:
		if is_instance_valid(img): _dim_targets.append(img)
	# 费用组（_cost_bg 描边 / _cost_rect 菱形 / _cost_label 数字）故意排除：
	# 未激活状态下费用仍需保持高亮可读，绝不被压暗（全局规则）。
	# 非当前角色时与卡面同受黑幕，随卡一起变暗（不享受费用组的高亮豁免）。
	if is_instance_valid(_name_label): _dim_targets.append(_name_label)
	if is_instance_valid(_type_label): _dim_targets.append(_type_label)
	if is_instance_valid(_effect_label): _dim_targets.append(_effect_label)
	if is_instance_valid(_desc_label): _dim_targets.append(_desc_label)

## 惰性构建共享灰幕着色器（每实例一次）。
func _ensure_dim_material() -> void:
	if _dim_material != null:
		return
	var sh := Shader.new()
	sh.code = DIM_SHADER
	_dim_material = ShaderMaterial.new()
	_dim_material.shader = sh

## 给所有叶子挂灰幕着色器（统一压暗）。绝不写任何子节点颜色属性。
func _attach_dim() -> void:
	_ensure_dim_material()
	for t in _dim_targets:
		var ci := t as CanvasItem
		if is_instance_valid(ci):
			ci.material = _dim_material

## 卸下所有叶子的灰幕着色器（还原默认渲染=基础原色）。
func _detach_dim() -> void:
	for t in _dim_targets:
		var ci := t as CanvasItem
		if is_instance_valid(ci):
			ci.material = null


## ── 伪 3D 倾斜已移除（2026-09-08）：原 fake3d 顶点透视着色器 + 整组 2D 变换方案被用户判定效果不理想，
## 已整体删除，改由 hand_card.gd 的 ClickSweep（一次性斜向流光）提供点击反馈。边框颜色恢复由 _apply_enhanced 控制的 modulate。──


## ── 边框流光已移除（2026-09-09）：play_frame_sweep / _make_sweep_line_texture 整体删除。用户判定方向不对；呼吸灯(breathing_frame)不适用于卡框。边框颜色逻辑(_apply_enhanced)保持不变。──


## ── 边框流光·蒙版版（2026-09-25，当前启用）：play_border_flow + BorderGlow 层 + BORDER_FLOW_SHADER。──
## 与 09-09 旧版的关键区别：旧版是「一条扫光线横穿卡面」（方向不对被否）；新版让高光带沿卡框「环游一圈」，
## 并以 card_frame 的 alpha 作蒙版 → 流光严格限制在边框形状内（中间透明区不上色）。render_mode blend_add 纯提亮。
## flash_border_boost（闪白）保留不删，可与流光叠加（在 hand_manager 触发点两行都调即可）。──

## 编辑器预览和真实手牌使用同一个数据绑定入口。
func apply_data(data: CardData, number: int, morale_bonus: int = 0) -> void:
	card_name = data.card_name
	card_type = data.card_type
	cost_color = data.uno_color
	cost_value = number
	rarity = data.rarity
	enhanced = data.enhanced
	card_effect = data.effect_text(number, morale_bonus)
	card_desc = data.description if not data.description.is_empty() else data.rules_text()
	card_face = load(data.art_path) as Texture2D if ResourceLoader.exists(data.art_path) else null
	if is_node_ready():
		_face.texture = card_face
		_face.visible = card_face != null
		_apply_all()
