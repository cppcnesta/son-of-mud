// 动画 CG 用：头发在水里柔软地起伏。发根（root 附近）不动，离发根越远、越往下，摆得越多；
// 两道不同周期的波沿头发传过去，再叠一点左右荡，不是整体旋转。图要四周留透明边，偏移时才不会被裁掉。
shader_type canvas_item;

uniform vec2 root = vec2(0.5, 0.15);       // 发根在图里的位置（0～1）
uniform float amp_y = 0.012;               // 上下起伏的幅度（占图高）
uniform float amp_x = 0.006;               // 左右荡的幅度（占图宽）
uniform float speed = 1.0;

float smooth01(float a, float b, float x) {
	float k = clamp((x - a) / (b - a), 0.0, 1.0);
	return k * k * (3.0 - 2.0 * k);
}

void fragment() {
	vec2 uv = UV;
	float s = TIME * speed;
	float d = abs(uv.x - root.x) / max(root.x, 1.0 - root.x);
	float wy = pow(smooth01(0.12, 1.0, d), 1.15);
	float dy = wy * (amp_y * sin(s * 1.1 - uv.x * 7.5) + amp_y * 0.5 * sin(s * 1.9 - uv.x * 13.0 + 1.3));
	float wx = pow(smooth01(root.y, 1.0, uv.y), 1.3);
	float dx = wx * (amp_x * sin(s * 0.9 - uv.y * 6.0 + 0.7) + amp_x * 0.5 * sin(s * 1.7 - uv.y * 11.0));
	vec2 src = uv - vec2(dx, dy);
	vec4 c = texture(TEXTURE, src);
	if (src.x < 0.0 || src.x > 1.0 || src.y < 0.0 || src.y > 1.0) c.a = 0.0;
	COLOR = c * COLOR;
}
