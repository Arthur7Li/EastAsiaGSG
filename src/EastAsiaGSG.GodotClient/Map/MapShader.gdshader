shader_type canvas_item;

// Static lossless RG8/RGB8 texture where ProvinceId = (R << 8) | G
uniform sampler2D provinces_tex : filter_nearest, repeat_disable;

// Dynamic 1D texture (2048x1, RGBA8):
// RGB = target display color for ProvinceId
// Alpha = encoded ControllerTag (tag / 255.0) for thick country border detection
uniform sampler2D map_lut_tex : filter_nearest, repeat_disable;

uniform int hovered_province_id = -1;
uniform int selected_province_id = -1;

uniform vec4 ocean_color : source_color = vec4(0.06, 0.13, 0.23, 1.0);
uniform vec4 shallow_ocean_color : source_color = vec4(0.11, 0.22, 0.36, 1.0);
uniform vec4 shoreline_color : source_color = vec4(0.04, 0.08, 0.14, 1.0);
uniform vec4 province_border_color : source_color = vec4(0.10, 0.12, 0.15, 0.42);
uniform vec4 country_border_color : source_color = vec4(0.05, 0.05, 0.06, 0.96);
uniform vec4 selected_border_color : source_color = vec4(1.0, 0.86, 0.18, 1.0);

int decode_province_id(vec4 sample_col) {
    int r = int(round(sample_col.r * 255.0));
    int g = int(round(sample_col.g * 255.0));
    return (r << 8) | g;
}

vec4 fetch_lut(int prov_id) {
    float u = (float(prov_id) + 0.5) / 2048.0;
    return texture(map_lut_tex, vec2(u, 0.5));
}

void fragment() {
    vec2 tex_size = vec2(textureSize(provinces_tex, 0));
    vec2 pixel_step = 1.0 / tex_size;

    vec4 center_raw = texture(provinces_tex, UV);
    int prov_id = decode_province_id(center_raw);

    // Ocean sentinel check (0xFFFF = 65535 or out of bounds >= 1500)
    if (prov_id >= 1500) {
        // Sample cardinal + diagonal rings for coastal bathymetry & shoreline ink
        int n1 = decode_province_id(texture(provinces_tex, UV + vec2(0.0, -pixel_step.y)));
        int s1 = decode_province_id(texture(provinces_tex, UV + vec2(0.0, pixel_step.y)));
        int e1 = decode_province_id(texture(provinces_tex, UV + vec2(pixel_step.x, 0.0)));
        int w1 = decode_province_id(texture(provinces_tex, UV + vec2(-pixel_step.x, 0.0)));

        bool adj_land_1 = (n1 < 1500) || (s1 < 1500) || (e1 < 1500) || (w1 < 1500);
        if (adj_land_1) {
            COLOR = shoreline_color;
        } else {
            int n4 = decode_province_id(texture(provinces_tex, UV + vec2(0.0, -pixel_step.y * 4.0)));
            int s4 = decode_province_id(texture(provinces_tex, UV + vec2(0.0, pixel_step.y * 4.0)));
            int e4 = decode_province_id(texture(provinces_tex, UV + vec2(pixel_step.x * 4.0, 0.0)));
            int w4 = decode_province_id(texture(provinces_tex, UV + vec2(-pixel_step.x * 4.0, 0.0)));
            int ne3 = decode_province_id(texture(provinces_tex, UV + vec2(pixel_step.x * 3.0, -pixel_step.y * 3.0)));
            int nw3 = decode_province_id(texture(provinces_tex, UV + vec2(-pixel_step.x * 3.0, -pixel_step.y * 3.0)));
            int se3 = decode_province_id(texture(provinces_tex, UV + vec2(pixel_step.x * 3.0, pixel_step.y * 3.0)));
            int sw3 = decode_province_id(texture(provinces_tex, UV + vec2(-pixel_step.x * 3.0, pixel_step.y * 3.0)));

            bool near_coast = (n4 < 1500) || (s4 < 1500) || (e4 < 1500) || (w4 < 1500)
                           || (ne3 < 1500) || (nw3 < 1500) || (se3 < 1500) || (sw3 < 1500);
            vec3 water_rgb = near_coast ? mix(ocean_color.rgb, shallow_ocean_color.rgb, 0.65) : ocean_color.rgb;
            COLOR = vec4(water_rgb, 1.0);
        }
    } else {
        vec4 lut_data = fetch_lut(prov_id);
        vec3 base_color = lut_data.rgb;
        int controller_tag = int(round(lut_data.a * 255.0));

        // Subtle operational paper grain
        vec2 pix_coord = UV * tex_size;
        float grain = fract(sin(dot(floor(pix_coord), vec2(12.9898, 78.233))) * 43758.5453);
        base_color *= (0.975 + 0.04 * grain);

        // Sample 4 cardinal neighbors (1px) and 4 outer/diagonal neighbors (2px)
        int id_n = decode_province_id(texture(provinces_tex, UV + vec2(0.0, -pixel_step.y)));
        int id_s = decode_province_id(texture(provinces_tex, UV + vec2(0.0, pixel_step.y)));
        int id_e = decode_province_id(texture(provinces_tex, UV + vec2(pixel_step.x, 0.0)));
        int id_w = decode_province_id(texture(provinces_tex, UV + vec2(-pixel_step.x, 0.0)));

        int id_n2 = decode_province_id(texture(provinces_tex, UV + vec2(0.0, -pixel_step.y * 2.0)));
        int id_s2 = decode_province_id(texture(provinces_tex, UV + vec2(0.0, pixel_step.y * 2.0)));
        int id_e2 = decode_province_id(texture(provinces_tex, UV + vec2(pixel_step.x * 2.0, 0.0)));
        int id_w2 = decode_province_id(texture(provinces_tex, UV + vec2(-pixel_step.x * 2.0, 0.0)));

        int id_ne = decode_province_id(texture(provinces_tex, UV + vec2(pixel_step.x, -pixel_step.y)));
        int id_nw = decode_province_id(texture(provinces_tex, UV + vec2(-pixel_step.x, -pixel_step.y)));
        int id_se = decode_province_id(texture(provinces_tex, UV + vec2(pixel_step.x, pixel_step.y)));
        int id_sw = decode_province_id(texture(provinces_tex, UV + vec2(-pixel_step.x, pixel_step.y)));

        bool is_coast_border = (id_n >= 1500) || (id_s >= 1500) || (id_e >= 1500) || (id_w >= 1500);
        bool is_prov_border = (id_n != prov_id) || (id_s != prov_id) || (id_e != prov_id) || (id_w != prov_id);
        bool is_country_border = false;

        int neighbors[12] = int[12](id_n, id_s, id_e, id_w, id_n2, id_s2, id_e2, id_w2, id_ne, id_nw, id_se, id_sw);
        for (int i = 0; i < 12; i++) {
            int nid = neighbors[i];
            if (nid != prov_id && nid < 1500) {
                int n_tag = int(round(fetch_lut(nid).a * 255.0));
                if (n_tag != controller_tag) {
                    is_country_border = true;
                    break;
                }
            }
        }

        if (prov_id == selected_province_id) {
            base_color = mix(base_color, vec3(1.0, 0.92, 0.45), 0.28);
        } else if (prov_id == hovered_province_id) {
            base_color = mix(base_color, vec3(1.0, 1.0, 1.0), 0.18);
        }

        if (prov_id == selected_province_id && is_prov_border) {
            base_color = selected_border_color.rgb;
        } else if (is_coast_border) {
            base_color = mix(base_color, shoreline_color.rgb, 0.85);
        } else if (is_country_border) {
            base_color = mix(base_color, country_border_color.rgb, country_border_color.a);
        } else if (is_prov_border) {
            base_color = mix(base_color, province_border_color.rgb, province_border_color.a);
        }

        COLOR = vec4(base_color, 1.0);
    }
}
