shader_type canvas_item;

uniform float water_level : hint_range(0.0, 1.0) = 0.0;
uniform float time_offset = 0.0;

uniform float wave_amplitude_1 = 0.012;
uniform float wave_frequency_1 = 5.5;
uniform float wave_speed_1     = 1.4;

uniform float wave_amplitude_2 = 0.006;
uniform float wave_frequency_2 = 9.0;
uniform float wave_speed_2     = 2.1;

uniform float foam_width = 0.018;

void fragment() {
    float uv_x = UV.x;
    float uv_y = UV.y;

    float surface_y = 1.0 - water_level;

    float wave  = sin(uv_x * wave_frequency_1 * TAU + time_offset * wave_speed_1) * wave_amplitude_1;
    wave       += sin(uv_x * wave_frequency_2 * TAU - time_offset * wave_speed_2 + 1.57) * wave_amplitude_2;
    wave       += sin(uv_x * 3.1 * TAU + time_offset * 0.7) * 0.003;

    float surface = surface_y + wave;

    if (uv_y < surface) {
        COLOR = vec4(0.0, 0.0, 0.0, 0.0);
    } else {
        float depth = clamp((uv_y - surface) / max(1.0 - surface, 0.001), 0.0, 1.0);

        vec3 color_low  = vec3(0.15, 0.45, 0.92);
        vec3 color_mid  = vec3(0.28, 0.22, 0.78);
        vec3 color_high = vec3(0.72, 0.08, 0.15);

        vec3 base_color;
        if (water_level < 0.6) {
            base_color = mix(color_low, color_mid, water_level / 0.6);
        } else {
            base_color = mix(color_mid, color_high, (water_level - 0.6) / 0.4);
        }

        float alpha = mix(0.28, 0.70, depth);

        float foam = smoothstep(foam_width, 0.0, uv_y - surface);
        base_color = mix(base_color, vec3(0.85, 0.92, 1.0), foam * 0.55);
        alpha = mix(alpha, 0.85, foam * 0.6);

        COLOR = vec4(base_color, alpha);
    }
}
