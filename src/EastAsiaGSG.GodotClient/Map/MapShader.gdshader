shader_type canvas_item;

// Static 16-bit RG encoded province ID map (Filter = Nearest, lossless):
// ProvinceId = (R << 8) | G. Ocean / unmapped sentinel = 65535 (R=255, G=255).
uniform sampler2D provinces_tex : filter_nearest, repeat_disable;

// Dynamic 1D Lookup Texture (Width = 2048, Height = 1, RGBA8, Filter = Nearest):
// RGB = target province color for active MapMode; Alpha = ControllerTag (0..14, or 255 for unowned).
uniform sampler2D map_lut_tex : filter_nearest, repeat_disable;

uniform int hovered_province_id = -1;
uniform int selected_province_id = -1;

uniform vec4 ocean_color : source_color = vec4(0.07, 0.15, 0.26, 1.0);
uniform vec4 province_border_color : source_color = vec4(0.10, 0.10, 0.10, 0.45);
uniform vec4 country_border_color : source_color = vec4(0.02, 0.02, 0.02, 0.92);
uniform vec4 coastline_border_color : source_color = vec4(0.04, 0.08, 0.14, 0.85);
uniform vec4 hovered_highlight : source_color = vec4(1.0, 1.0, 1.0, 0.18);
uniform vec4 selected_highlight : source_color = vec4(1.0, 0.86, 0.22, 0.32);

const int MAX_VALID_PROVINCES = 1500;
const int OCEAN_PROVINCE_ID = 65535;

int decode_province_id(vec4 sample_col) {
	int r = int(round(sample_col.r * 255.0));
	int g = int(round(sample_col.g * 255.0));
	return (r << 8) | g;
}

vec4 fetch_province_lut(int province_id) {
	int clamped_id = clamp(province_id, 0, 2047);
	return texelFetch(map_lut_tex, ivec2(clamped_id, 0), 0);
}

int decode_controller_tag(vec4 lut_entry) {
	return int(round(lut_entry.a * 255.0));
}

void fragment() {
	vec4 center_id_texel = texture(provinces_tex, UV);
	int center_id = decode_province_id(center_id_texel);

	if (center_id >= MAX_VALID_PROVINCES || center_id == OCEAN_PROVINCE_ID) {
		COLOR = ocean_color;
	} else {
		vec4 center_lut = fetch_province_lut(center_id);
		vec3 final_rgb = center_lut.rgb;
		int center_controller = decode_controller_tag(center_lut);

		vec2 texel_size = 1.0 / vec2(textureSize(provinces_tex, 0));

		// Sample 4 cardinal 1-texel neighbors for province/country/coastline borders
		int id_n1 = decode_province_id(texture(provinces_tex, clamp(UV + vec2(0.0, -texel_size.y), vec2(0.0), vec2(1.0))));
		int id_s1 = decode_province_id(texture(provinces_tex, clamp(UV + vec2(0.0,  texel_size.y), vec2(0.0), vec2(1.0))));
		int id_e1 = decode_province_id(texture(provinces_tex, clamp(UV + vec2( texel_size.x, 0.0), vec2(0.0), vec2(1.0))));
		int id_w1 = decode_province_id(texture(provinces_tex, clamp(UV + vec2(-texel_size.x, 0.0), vec2(0.0), vec2(1.0))));

		// Sample 4 cardinal 2-texel neighbors so country borders render thicker than internal province borders
		int id_n2 = decode_province_id(texture(provinces_tex, clamp(UV + vec2(0.0, -2.0 * texel_size.y), vec2(0.0), vec2(1.0))));
		int id_s2 = decode_province_id(texture(provinces_tex, clamp(UV + vec2(0.0,  2.0 * texel_size.y), vec2(0.0), vec2(1.0))));
		int id_e2 = decode_province_id(texture(provinces_tex, clamp(UV + vec2( 2.0 * texel_size.x, 0.0), vec2(0.0), vec2(1.0))));
		int id_w2 = decode_province_id(texture(provinces_tex, clamp(UV + vec2(-2.0 * texel_size.x, 0.0), vec2(0.0), vec2(1.0))));

		bool is_coastline = false;
		bool is_country_border = false;
		bool is_province_border = false;

		int neighbors1[4] = int[4](id_n1, id_s1, id_e1, id_w1);
		for (int i = 0; i < 4; i++) {
			int n_id = neighbors1[i];
			if (n_id != center_id) {
				if (n_id >= MAX_VALID_PROVINCES || n_id == OCEAN_PROVINCE_ID) {
					is_coastline = true;
				} else {
					int n_ctrl = decode_controller_tag(fetch_province_lut(n_id));
					if (n_ctrl != center_controller) {
						is_country_border = true;
					} else {
						is_province_border = true;
					}
				}
			}
		}

		if (!is_country_border) {
			int neighbors2[4] = int[4](id_n2, id_s2, id_e2, id_w2);
			for (int i = 0; i < 4; i++) {
				int n_id = neighbors2[i];
				if (n_id < MAX_VALID_PROVINCES && n_id != center_id) {
					int n_ctrl = decode_controller_tag(fetch_province_lut(n_id));
					if (n_ctrl != center_controller) {
						is_country_border = true;
					}
				}
			}
		}

		// Apply selection / hover highlights prior to border ink
		if (center_id == selected_province_id) {
			final_rgb = mix(final_rgb, selected_highlight.rgb, selected_highlight.a);
		} else if (center_id == hovered_province_id) {
			final_rgb = mix(final_rgb, hovered_highlight.rgb, hovered_highlight.a);
		}

		// Apply prioritized border ink: Country Border > Coastline > Province Border
		if (is_country_border) {
			final_rgb = mix(final_rgb, country_border_color.rgb, country_border_color.a);
		} else if (is_coastline) {
			final_rgb = mix(final_rgb, coastline_border_color.rgb, coastline_border_color.a);
		} else if (is_province_border) {
			if (center_id == selected_province_id) {
				final_rgb = mix(final_rgb, selected_highlight.rgb, 0.75);
			} else {
				final_rgb = mix(final_rgb, province_border_color.rgb, province_border_color.a);
			}
		}

		COLOR = vec4(final_rgb, 1.0);
	}
}
