// 动画 CG 的极光：几条随时间起伏、变色的光带（加在画面上，相加混合）。
shader_type canvas_item;
render_mode blend_add;

uniform float intensity : hint_range(0.0, 2.0) = 0.85;
uniform float speed = 1.0;

vec3 hsv2rgb(vec3 c) {
	vec3 p = abs(fract(c.xxx + vec3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
	return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}

float band(vec2 uv, float y, float amp, float len, float spd, float w, float t) {
	float yc = y + amp * sin(uv.x * 6.2832 * len + t * spd) + 0.02 * sin(uv.x * 13.0 + t * 0.7);
	float h = w * (0.7 + 0.3 * sin(uv.x * 7.0 + t * 0.5));
	float d = (yc - uv.y) / h;          // 光带上方渐隐，下沿较硬
	return smoothstep(0.0, 0.6, d) * (1.0 - smoothstep(0.6, 1.0, d)) + smoothstep(-0.4, 0.0, d) * step(d, 0.0) * 0.6;
}

void fragment() {
	float t = TIME * speed;
	vec2 uv = UV;
	vec3 col = vec3(0.0);
	float a1 = band(uv, 0.18, 0.05, 1.3, 0.7, 0.16, t);
	float a2 = band(uv, 0.28, 0.06, 0.9, 0.5, 0.20, t);
	float a3 = band(uv, 0.12, 0.04, 1.7, 0.9, 0.12, t);
	col += hsv2rgb(vec3((170.0 + 40.0 * sin(t * 0.09 + 1.8)) / 360.0, 0.8, 0.65)) * a1 * (0.10 + 0.08 * sin(uv.x * 5.0 + t * 0.3));
	col += hsv2rgb(vec3((200.0 + 40.0 * sin(t * 0.09 + 2.8)) / 360.0, 0.85, 0.65)) * a2 * (0.10 + 0.08 * sin(uv.x * 5.0 + t * 0.3 + 1.0));
	col += hsv2rgb(vec3((290.0 + 40.0 * sin(t * 0.09 + 1.2)) / 360.0, 0.8, 0.6)) * a3 * (0.10 + 0.08 * sin(uv.x * 5.0 + t * 0.3 + 2.0));
	COLOR = vec4(col * intensity, 1.0);
}
