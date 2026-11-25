// CG 图层的 PSD 混合模式（Godot 自带的只有正常 / 相加 / 相减 / 相乘）。
// mode：0 滤色（Screen）、1 颜色减淡（Color Dodge）、2 颜色（Color）。透明度用节点的 Modulate 或 strength 调。
// 用到屏幕上已经画好的内容，所以这个图层前面要放一个 BackBufferCopy（复制整个视口）。
shader_type canvas_item;

uniform int mode : hint_range(0, 2) = 0;
uniform float strength : hint_range(0.0, 1.0) = 1.0;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;

float lum(vec3 c) { return dot(c, vec3(0.3, 0.59, 0.11)); }

vec3 clip_color(vec3 c) {
	float l = lum(c);
	float n = min(c.r, min(c.g, c.b));
	float x = max(c.r, max(c.g, c.b));
	if (n < 0.0) c = l + (c - l) * l / max(l - n, 1e-5);
	if (x > 1.0) c = l + (c - l) * (1.0 - l) / max(x - l, 1e-5);
	return c;
}

void fragment() {
	vec4 src = texture(TEXTURE, UV) * COLOR;
	vec3 dst = texture(screen_tex, SCREEN_UV).rgb;
	vec3 res;
	if (mode == 0) {
		res = 1.0 - (1.0 - dst) * (1.0 - src.rgb);
	} else if (mode == 1) {
		res = min(dst / max(1.0 - src.rgb, vec3(1e-3)), vec3(1.0));
	} else {
		res = clip_color(src.rgb + (lum(dst) - lum(src.rgb)));
	}
	COLOR = vec4(res, src.a * strength);
}
