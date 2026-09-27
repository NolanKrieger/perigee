shader_type canvas_item;
render_mode blend_mix;

// Atmosphere glow: per-pixel exponential falloff from each body's surface (GDD §13/§17). Up to 8 bodies.
uniform int count = 0;
uniform vec4 bodies[8];   // x, y = centre in canvas px (y down); z = surface radius px; w = scale height px
uniform vec4 colors[8];   // rgb, a = alpha at the surface
uniform vec2 screen = vec2(1920.0, 1080.0);

void fragment() {
    vec2 p = UV * screen;
    vec3 rgb = vec3(0.0);
    float alpha = 0.0;
    for (int i = 0; i < 8; i++) {
        if (i >= count) break;
        vec2 c = bodies[i].xy;
        float R = bodies[i].z;
        float H = bodies[i].w;
        float d = length(p - c) - R;
        if (d < 0.0) d = 0.0;
        float a = colors[i].a * exp(-d / H);
        rgb += colors[i].rgb * a;
        alpha += a;
    }
    COLOR = vec4(rgb / max(alpha, 1e-4), min(alpha, 1.0));
}
