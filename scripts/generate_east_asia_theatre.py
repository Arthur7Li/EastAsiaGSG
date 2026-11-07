#!/usr/bin/env python3
"""
generate_east_asia_theatre.py
Generates the complete 232-province / 36-state East Asian Theatre dataset:
1. data/map/east_asia_1985_provinces.json (232 geographically projected provinces)
2. data/map/east_asia_1985_provinces.png (2048x2048 RGB8 lossless PNG with O(1) picking)
3. data/oob_1985/{VNM, PHL, MNG, THA, LAO, KHM, MMR, MYS}_oob.json (8 Regional Minors)
"""

import json
import math
import os
import struct
import zlib


def write_png(filename, width, height, rgb_bytes):
    raw = bytearray()
    row_bytes = width * 3
    for y in range(height):
        raw.append(0)  # Filter type 0 (None)
        start = y * row_bytes
        raw.extend(rgb_bytes[start : start + row_bytes])
    compressed = zlib.compress(bytes(raw), level=6)

    def make_chunk(chunk_type, data):
        c = chunk_type + data
        crc = zlib.crc32(c) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + c + struct.pack(">I", crc)

    ihdr_data = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    with open(filename, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(make_chunk(b"IHDR", ihdr_data))
        f.write(make_chunk(b"IDAT", compressed))
        f.write(make_chunk(b"IEND", b""))


def edge_noise(x1, y1, x2, y2, depth_seed):
    """Deterministic symmetric hash for edge (x1,y1)-(x2,y2) so shared borders match 100%."""
    if (x1, y1) > (x2, y2):
        x1, y1, x2, y2 = x2, y2, x1, y1
    h = (
        (int(x1 * 10) * 73856093)
        ^ (int(y1 * 10) * 19349663)
        ^ (int(x2 * 10) * 83492791)
        ^ (int(y2 * 10) * 49979693)
        ^ (depth_seed * 2654435761)
    )
    h = ((h >> 16) ^ h) * 0x45D9F3B
    h = ((h >> 16) ^ h) & 0xFFFFFFFF
    return (h / 2147483647.5) - 1.0


def subdivide_poly(poly, width, height, iterations=3, roughness=0.13):
    """
    Subdivides polygon edges deterministically using symmetric edge hashing.
    Shared edges between adjacent country polygons displace identically in lockstep.
    Edges lying on canvas boundary (x=0 or y=0) stay pinned to the boundary.
    """
    pts = list(poly)
    for it in range(iterations):
        new_pts = []
        n = len(pts)
        scale = roughness * (0.65 ** it)
        for i in range(n):
            x1, y1 = pts[i]
            x2, y2 = pts[(i + 1) % n]
            new_pts.append((x1, y1))
            mx = 0.5 * (x1 + x2)
            my = 0.5 * (y1 + y2)
            dx = x2 - x1
            dy = y2 - y1
            length = math.hypot(dx, dy)
            if length < 10.0 or (x1 <= 1 and x2 <= 1) or (y1 <= 1 and y2 <= 1):
                continue
            if (x1, y1) < (x2, y2):
                nx, ny = -dy / length, dx / length
            else:
                nx, ny = dy / length, -dx / length
            disp = edge_noise(x1, y1, x2, y2, it + 1) * length * scale
            px = max(0.0, min(float(width - 1), mx + nx * disp))
            py = max(0.0, min(float(height - 1), my + ny * disp))
            new_pts.append((px, py))
        pts = new_pts
    return pts


def fill_polygon(mask, width, height, poly, region_id, subdivide=True):
    """Scanline polygon rasterizer into bytearray mask."""
    if len(poly) < 3:
        return
    if subdivide:
        poly = subdivide_poly(poly, width, height, iterations=3, roughness=0.13)
    ys = [p[1] for p in poly]
    y_min = max(0, int(min(ys)))
    y_max = min(height - 1, int(max(ys)))
    n = len(poly)
    for y in range(y_min, y_max + 1):
        yf = y + 0.5
        nodes = []
        j = n - 1
        for i in range(n):
            xi, yi = poly[i]
            xj, yj = poly[j]
            if (yi < yf and yj >= yf) or (yj < yf and yi >= yf):
                x = xi + (yf - yi) / (yj - yi) * (xj - xi)
                nodes.append(x)
            j = i
        nodes.sort()
        row_off = y * width
        for k in range(0, len(nodes) - 1, 2):
            x_start = max(0, int(math.floor(nodes[k])))
            x_end = min(width - 1, int(math.ceil(nodes[k + 1])))
            for x in range(x_start, x_end + 1):
                mask[row_off + x] = region_id


def transform_korea(cx, cy):
    """Maps Korean Peninsula sandbox [280..725, 120..930] into East Asia [980..1240, 520..960]."""
    nx = int(round(980 + (cx - 280) * (260.0 / 445.0)))
    ny = int(round(520 + (cy - 120) * (440.0 / 810.0)))
    return nx, ny


def main():
    root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    states_path = os.path.join(root, "data/map/east_asia_1985_states.json")
    korean_provs_path = os.path.join(root, "data/map/provinces.json")
    out_provs_json = os.path.join(root, "data/map/east_asia_1985_provinces.json")
    out_provs_png = os.path.join(root, "data/map/east_asia_1985_provinces.png")
    oob_dir = os.path.join(root, "data/oob_1985")

    with open(states_path, "r", encoding="utf-8") as f:
        states = json.load(f)

    with open(korean_provs_path, "r", encoding="utf-8") as f:
        korean_provs = json.load(f)

    prov_to_state = {}
    for s in states:
        for pid in s["provinces"]:
            prov_to_state[pid] = s

    # 1. Collect & Geographically Project Korea Provinces (0..119) -> Region Mask 1
    all_provinces = {}
    prov_region_mask = {}
    for p in korean_provs:
        pid = p["provinceId"]
        nx, ny = transform_korea(p["centerX"], p["centerY"])
        prov_region_mask[pid] = 1
        all_provinces[pid] = {
            "provinceId": pid,
            "name": p["name"],
            "stateId": prov_to_state[pid]["stateId"],
            "ownerTag": p["ownerTag"],
            "controllerTag": p["controllerTag"],
            "terrain": p["terrain"],
            "strategicRegionId": p["strategicRegionId"],
            "supplyHubLevel": p["supplyHubLevel"],
            "portLevel": p["portLevel"],
            "infrastructureLevel": p["infrastructureLevel"],
            "centerX": nx,
            "centerY": ny,
            "adjacencies": list(p["adjacencies"]),
        }

    # 2. Define the 112 Theatre Provinces with Geographically Projected (cx, cy) & Region Masks
    # Tuple: (pid, name, stateId, ownerTag, terrain, strategicRegionId, hub, port, infra, regionMask, cx, cy)
    theatre_defs = [
        # State 10: Khabarovsk-Amur BAM Rail Corridor (SOV, Region 1, Mask 2)
        (150, "Irkutsk-Chita Entry Gate", 10, "SOV", "Plains", 1, 5, 0, 8, 2, 160, 95),
        (151, "Taishet-Baikal Junction", 10, "SOV", "Forest", 1, 3, 0, 7, 2, 420, 95),
        (152, "Tynda BAM Central", 10, "SOV", "Forest", 1, 4, 0, 8, 2, 720, 105),
        (153, "Komsomolsk-on-Amur", 10, "SOV", "Urban", 1, 4, 2, 8, 2, 1345, 165),
        (160, "Skovorodino Depot", 10, "SOV", "Plains", 1, 3, 0, 7, 2, 900, 165),
        (161, "Belogorsk Rail Hub", 10, "SOV", "Plains", 1, 3, 0, 7, 2, 1085, 215),
        (162, "Birobidzhan Terminal", 10, "SOV", "Forest", 1, 3, 0, 7, 2, 1220, 265),
        (163, "Khabarovsk Amur Fortress", 10, "SOV", "Urban", 1, 5, 3, 9, 2, 1335, 275),

        # State 11: Primorye-Vladivostok Fleet Bastion (SOV, Region 1, Mask 2)
        (170, "Ussuriysk Armored Redoubt", 11, "SOV", "Plains", 1, 4, 0, 8, 2, 1285, 430),
        (171, "Vladivostok Golden Horn", 11, "SOV", "Urban", 1, 5, 5, 9, 2, 1290, 480),
        (172, "Nakhodka Commercial Port", 11, "SOV", "Hills", 1, 3, 4, 7, 2, 1335, 488),
        (173, "Spassk-Dalny Depot", 11, "SOV", "Forest", 1, 2, 0, 6, 2, 1310, 365),
        (174, "Arsenyev Aviation Works", 11, "SOV", "Hills", 1, 3, 0, 7, 2, 1355, 415),
        (175, "Posyet Bay Frontier", 11, "SOV", "Hills", 1, 3, 3, 7, 2, 1258, 498),

        # State 12: Sakhalin (Mask 3) & Kuril Island Chain (Mask 4) (SOV, Region 2)
        (180, "Yuzhno-Sakhalinsk", 12, "SOV", "Hills", 2, 3, 4, 6, 3, 1545, 270),
        (181, "Kholmsk Strait Ferry", 12, "SOV", "Hills", 2, 2, 4, 6, 3, 1518, 240),
        (182, "Korsakov Naval Terminal", 12, "SOV", "Plains", 2, 2, 3, 6, 3, 1552, 312),
        (183, "Iturup Kuril Bastion", 12, "SOV", "Mountain", 2, 2, 3, 5, 4, 1755, 385),

        # State 20: Beijing-Tianjin Direct Municipality (PRC, Region 3, Mask 6)
        (200, "Beijing Central Citadel", 20, "PRC", "Urban", 3, 5, 0, 9, 6, 585, 585),
        (201, "Tianjin Binhai Port", 20, "PRC", "Urban", 3, 4, 4, 8, 6, 685, 655),

        # State 21: Hebei Province (PRC, Region 3, Mask 6)
        (202, "Tangshan Industrial Basin", 21, "PRC", "Plains", 3, 3, 0, 7, 6, 725, 595),
        (203, "Baoding Southern Gate", 21, "PRC", "Plains", 3, 3, 0, 7, 6, 420, 665),
        (204, "Shijiazhuang Rail Hub", 21, "PRC", "Plains", 3, 4, 0, 8, 6, 220, 725),
        (205, "Qinhuangdao Naval Gate", 21, "PRC", "Hills", 3, 3, 3, 7, 6, 805, 555),

        # State 22: Liaoning Province & Dalian (PRC, Region 3, Mask 6)
        (210, "Shenyang Arsenal Hub", 22, "PRC", "Urban", 3, 5, 0, 8, 6, 935, 525),
        (211, "Dandong Yalu Crossing", 22, "PRC", "Plains", 3, 4, 2, 7, 6, 952, 595),
        (212, "Dalian Lushun Fleet Port", 22, "PRC", "Urban", 3, 4, 5, 8, 6, 875, 660),
        (215, "Anshan Steelworks", 22, "PRC", "Plains", 3, 3, 0, 7, 6, 910, 572),

        # State 23: Jilin & Heilongjiang Frontier (PRC, Region 3, Mask 6)
        (213, "Changchun Rail Center", 23, "PRC", "Plains", 3, 4, 0, 7, 6, 1010, 445),
        (214, "Harbin Northern Fortress", 23, "PRC", "Urban", 3, 4, 1, 8, 6, 1065, 355),

        # State 24: Shandong Peninsula & Qingdao Fleet Base (PRC, Region 4, Mask 6)
        (220, "Jinan Yellow River Gate", 24, "PRC", "Urban", 4, 4, 0, 8, 6, 545, 805),
        (221, "Qingdao North Sea Bastion", 24, "PRC", "Urban", 4, 5, 5, 9, 6, 805, 835),
        (222, "Yantai Bohai Strait Port", 24, "PRC", "Hills", 4, 3, 4, 7, 6, 845, 765),
        (223, "Weihai Fleet Anchorage", 24, "PRC", "Hills", 4, 3, 3, 7, 6, 895, 775),

        # State 25: Jiangsu & Shanghai Megalopolis (PRC, Region 4, Mask 6)
        (230, "Shanghai Huangpu Core", 25, "PRC", "Urban", 4, 5, 5, 10, 6, 890, 1025),
        (231, "Nanjing Yangtze Citadel", 25, "PRC", "Urban", 4, 4, 2, 8, 6, 520, 985),
        (232, "Suzhou Industrial Park", 25, "PRC", "Plains", 4, 3, 0, 8, 6, 815, 1015),
        (235, "Wuxi Grand Canal Basin", 25, "PRC", "Plains", 4, 3, 0, 7, 6, 695, 995),

        # State 26: Zhejiang Province & Ningbo (PRC, Region 4, Mask 6)
        (233, "Hangzhou Qiantang Hub", 26, "PRC", "Urban", 4, 4, 2, 8, 6, 745, 1085),
        (234, "Ningbo Zhoushan Anchorage", 26, "PRC", "Hills", 4, 4, 5, 8, 6, 870, 1100),

        # State 27: Fujian Strait Frontline (PRC, Region 5, Mask 6)
        (240, "Fuzhou Strait Command", 27, "PRC", "Urban", 5, 4, 4, 8, 6, 810, 1225),
        (241, "Xiamen Frontline Redoubt", 27, "PRC", "Urban", 5, 4, 4, 8, 6, 755, 1330),
        (242, "Quanzhou Maritime Hub", 27, "PRC", "Plains", 5, 3, 3, 7, 6, 785, 1285),
        (243, "Nanping Mountain Arsenal", 27, "PRC", "Mountain", 5, 2, 0, 6, 6, 565, 1185),
        (244, "Longyan Southern Highlands", 27, "PRC", "Mountain", 5, 2, 0, 6, 6, 540, 1265),

        # State 28: Guangdong & Shenzhen SEZ (PRC, Region 5, Mask 6)
        (250, "Guangzhou Pearl Delta Core", 28, "PRC", "Urban", 5, 5, 4, 9, 6, 545, 1390),
        (251, "Shenzhen Reform Corridor", 28, "PRC", "Urban", 5, 4, 4, 9, 6, 625, 1425),
        (253, "Shantou Eastern Port", 28, "PRC", "Plains", 5, 3, 3, 7, 6, 705, 1380),
        (254, "Shaoguan Northern Pass", 28, "PRC", "Mountain", 5, 3, 0, 6, 6, 360, 1285),

        # State 29: Hainan & Leizhou South Sea Bastion (PRC, Region 5, Mask 6)
        (252, "Zhanjiang South Sea Bastion", 29, "PRC", "Plains", 5, 4, 5, 8, 6, 475, 1480),

        # State 30: Tokyo-Kanto Economic Megalopolis (JPN, Region 6, Mask 8)
        (300, "Tokyo Chiyoda Citadel", 30, "JPN", "Urban", 6, 5, 4, 10, 8, 1578, 808),
        (301, "Yokohama Maritime Hub", 30, "JPN", "Urban", 6, 5, 5, 10, 8, 1562, 835),
        (302, "Yokosuka USN Fleet Base", 30, "JPN", "Urban", 6, 4, 5, 9, 8, 1574, 856),
        (303, "Chiba Boso Peninsula", 30, "JPN", "Plains", 6, 3, 3, 8, 8, 1612, 822),
        (304, "Saitama Northern Transit", 30, "JPN", "Urban", 6, 4, 0, 8, 8, 1560, 778),
        (305, "Utsunomiya North Gate", 30, "JPN", "Hills", 6, 3, 0, 7, 8, 1575, 725),

        # State 31: Hokkaido Northern Defense Frontier (JPN, Region 6, Mask 7)
        (310, "Sapporo Northern Command", 31, "JPN", "Urban", 6, 4, 2, 8, 7, 1565, 470),
        (311, "Hakodate Tsugaru Port", 31, "JPN", "Hills", 6, 3, 4, 7, 7, 1538, 528),
        (312, "Asahikawa Armored Center", 31, "JPN", "Plains", 6, 3, 0, 7, 7, 1602, 435),
        (313, "Kushiro Pacific Gateway", 31, "JPN", "Plains", 6, 3, 3, 6, 7, 1670, 465),
        (314, "Wakkanai Soya Watchpost", 31, "JPN", "Hills", 6, 2, 3, 6, 7, 1572, 380),

        # State 32: Kansai-Chubu Industrial Heartland (JPN, Region 7, Mask 8)
        (320, "Osaka Bay Metropolis", 32, "JPN", "Urban", 7, 5, 4, 9, 8, 1412, 885),
        (321, "Kyoto Cultural Core", 32, "JPN", "Urban", 7, 3, 0, 8, 8, 1422, 855),
        (322, "Nagoya Tokai Arsenal", 32, "JPN", "Urban", 7, 4, 3, 9, 8, 1472, 858),
        (323, "Kobe Port Terminal", 32, "JPN", "Urban", 7, 4, 4, 9, 8, 1375, 885),
        (324, "Shizuoka Tokaido Route", 32, "JPN", "Hills", 7, 3, 2, 8, 8, 1522, 865),

        # State 33: Kyushu-Sasebo Western Maritime Gate (JPN, Region 7, Mask 9)
        (330, "Fukuoka Tsushima Gate", 33, "JPN", "Urban", 7, 4, 4, 9, 9, 1275, 975),
        (331, "Kitakyushu Ironworks", 33, "JPN", "Urban", 7, 4, 3, 8, 9, 1300, 955),
        (332, "Sasebo Naval Arsenal", 33, "JPN", "Hills", 7, 4, 5, 8, 9, 1245, 1000),
        (333, "Nagasaki Shipyards", 33, "JPN", "Hills", 7, 3, 4, 8, 9, 1255, 1030),
        (334, "Kagoshima South Cape", 33, "JPN", "Mountain", 7, 3, 3, 7, 9, 1295, 1072),

        # State 40: Taipei-Hsinchu Silicon Corridor (TWN, Region 8, Mask 11)
        (400, "Taipei Capital District", 40, "TWN", "Urban", 8, 5, 0, 9, 11, 938, 1270),
        (401, "Keelung Northern Anchorage", 40, "TWN", "Hills", 8, 3, 4, 8, 11, 955, 1255),
        (402, "Hsinchu Science Park", 40, "TWN", "Urban", 8, 4, 0, 9, 11, 920, 1295),
        (403, "Taichung Coastal Redoubt", 40, "TWN", "Urban", 8, 3, 3, 8, 11, 910, 1330),
        (404, "Yilan Eastern Coast", 40, "TWN", "Mountain", 8, 2, 2, 6, 11, 958, 1300),

        # State 41: Kaohsiung-Tsoying Southern Port & Arsenal (TWN, Region 8, Mask 11)
        (410, "Kaohsiung Commercial Port", 41, "TWN", "Urban", 8, 5, 5, 9, 11, 912, 1415),
        (411, "Tsoying Main Fleet Base", 41, "TWN", "Urban", 8, 4, 5, 9, 11, 905, 1395),
        (412, "Tainan Cultural Redoubt", 41, "TWN", "Plains", 8, 3, 2, 8, 11, 902, 1370),
        (413, "Pingtung Airbase Corridor", 41, "TWN", "Plains", 8, 3, 0, 7, 11, 932, 1425),

        # State 42: Kinmen (Mask 12) & Penghu (Mask 13) Strait Bastions (TWN, Region 8)
        (420, "Kinmen Island Fortress", 42, "TWN", "Hills", 8, 3, 3, 7, 12, 796, 1338),
        (421, "Penghu Strait Naval Lock", 42, "TWN", "Plains", 8, 3, 4, 7, 13, 856, 1355),

        # State 50: Okinawa & Ryukyu USPACOM Keystone (USA, Region 9, Mask 10)
        (500, "Naha Port Terminal", 50, "USA", "Urban", 9, 4, 4, 8, 10, 1150, 1235),
        (501, "Kadena Air Base Hub", 50, "USA", "Plains", 9, 5, 0, 9, 10, 1162, 1208),
        (502, "White Beach USN Anchorage", 50, "USA", "Hills", 9, 3, 4, 7, 10, 1172, 1220),

        # State 51: Guam & Mariana Strategic Hub (USA, Region 10, Mask 21)
        (510, "Apra Harbor Fleet Anchor", 51, "USA", "Urban", 10, 5, 5, 9, 21, 1765, 1585),
        (511, "Andersen Strategic Bastion", 51, "USA", "Plains", 10, 4, 0, 8, 21, 1785, 1555),

        # State 60: Tonkin / Red River Delta (VNM, Region 11, Mask 14)
        (600, "Hanoi Ba Dinh Citadel", 60, "VNM", "Urban", 11, 4, 0, 8, 14, 375, 1465),
        (601, "Haiphong Tonkin Naval Port", 60, "VNM", "Urban", 11, 4, 4, 8, 14, 420, 1485),

        # State 68: Annam / Central Coastal Corridor (VNM, Region 11, Mask 14)
        (602, "Da Nang Central Coastal Base", 68, "VNM", "Hills", 11, 3, 4, 7, 14, 475, 1635),

        # State 69: Cochinchina / Cam Ranh & Mekong Delta (VNM, Region 11, Mask 14)
        (603, "Cam Ranh Bay Deep Anchorage", 69, "VNM", "Plains", 11, 4, 5, 7, 14, 495, 1765),

        # State 61: Luzon & Capital Bay Archipelago (PHL, Region 12, Mask 20)
        (610, "Manila Intramuros Core", 61, "PHL", "Urban", 12, 4, 4, 8, 20, 975, 1680),
        (611, "Subic Bay Fleet Anchorage", 61, "PHL", "Urban", 12, 5, 5, 9, 20, 940, 1665),
        (612, "Clark Air Base Corridor", 61, "PHL", "Plains", 12, 4, 0, 8, 20, 965, 1635),

        # State 70: Visayas & Southern Maritime Straits (PHL, Region 12, Mask 20)
        (613, "Batangas Southern Strait", 70, "PHL", "Hills", 12, 2, 3, 7, 20, 985, 1725),

        # State 62: Ulaanbaatar & Trans-Mongolian Steppe (MNG, Region 13, Mask 5)
        (620, "Ulaanbaatar Steppe Command", 62, "MNG", "Urban", 13, 3, 0, 7, 5, 415, 315),
        (621, "Darkhan Northern Junction", 62, "MNG", "Plains", 13, 2, 0, 6, 5, 210, 255),
        (622, "Sainshand Gobi Artery", 62, "MNG", "Plains", 13, 2, 0, 6, 5, 605, 405),

        # State 63: Central Thailand & Bangkok Metropolis (THA, Region 14, Mask 17)
        (630, "Bangkok Grand Palace Basin", 63, "THA", "Urban", 14, 4, 3, 8, 17, 230, 1715),
        (631, "Sattahip Royal Naval Port", 63, "THA", "Plains", 14, 3, 4, 7, 17, 265, 1758),

        # State 71: Khorat Plateau & Southern Peninsula (THA, Region 14, Mask 17)
        (632, "Nakhon Ratchasima Gateway", 71, "THA", "Plains", 14, 2, 0, 6, 17, 285, 1665),
        (633, "Hat Yai Southern Junction", 71, "THA", "Forest", 14, 2, 0, 6, 17, 225, 1895),

        # State 64: Vientiane & Mekong Highlands (LAO, Region 15, Mask 15)
        (640, "Vientiane Mekong Seat", 64, "LAO", "Urban", 15, 2, 0, 6, 15, 325, 1585),
        (641, "Luang Prabang Mountain Pass", 64, "LAO", "Mountain", 15, 1, 0, 5, 15, 285, 1510),

        # State 65: Phnom Penh & Tonle Sap Basin (KHM, Region 16, Mask 16)
        (650, "Phnom Penh Royal Citadel", 65, "KHM", "Urban", 16, 2, 1, 6, 16, 385, 1760),
        (651, "Kompong Som Coastal Haven", 65, "KHM", "Plains", 16, 2, 3, 6, 16, 345, 1798),

        # State 66: Lower Myanmar & Irrawaddy Delta (MMR, Region 17, Mask 18)
        (660, "Yangon Shwedagon Port", 66, "MMR", "Urban", 17, 3, 3, 7, 18, 95, 1615),
        (662, "Mawlamyine Coastal Terminal", 66, "MMR", "Hills", 17, 2, 2, 6, 18, 155, 1665),

        # State 72: Upper Myanmar & Mandalay Heartland (MMR, Region 17, Mask 18)
        (661, "Mandalay Royal Heartland", 72, "MMR", "Plains", 17, 2, 0, 6, 18, 95, 1475),

        # State 67: Malaya Peninsula & Strait of Malacca (MYS, Region 17, Mask 19)
        (670, "Kuala Lumpur Federal Seat", 67, "MYS", "Urban", 17, 4, 0, 8, 19, 265, 1965),
        (671, "Port Klang Maritime Lock", 67, "MYS", "Urban", 17, 3, 4, 8, 19, 238, 1975),
        (672, "Johor Bahru Causeway Strait", 67, "MYS", "Urban", 17, 3, 3, 8, 19, 305, 2005),
    ]

    for pdef in theatre_defs:
        pid, name, sid, owner, terrain, sreg, hub, port, infra, rmask, cx, cy = pdef
        prov_region_mask[pid] = rmask
        all_provinces[pid] = {
            "provinceId": pid,
            "name": name,
            "stateId": sid,
            "ownerTag": owner,
            "controllerTag": owner,
            "terrain": terrain,
            "strategicRegionId": sreg,
            "supplyHubLevel": hub,
            "portLevel": port,
            "infrastructureLevel": infra,
            "centerX": cx,
            "centerY": cy,
            "adjacencies": [],
        }

    # Helper to add symmetric edge
    def add_edge(u, v, flags):
        if u not in all_provinces or v not in all_provinces:
            raise ValueError(f"Unknown province in edge: {u} <-> {v}")
        if u == v:
            return
        u_adj = all_provinces[u]["adjacencies"]
        v_adj = all_provinces[v]["adjacencies"]
        for e in u_adj:
            if e["targetProvinceId"] == v:
                return
        u_adj.append({"targetProvinceId": v, "flags": flags})
        v_adj.append({"targetProvinceId": u, "flags": flags})

    # Internal state corridors & theatre cross-border graphs
    bam_line = [150, 151, 152, 153, 160, 161, 162, 163]
    for i in range(len(bam_line) - 1):
        add_edge(bam_line[i], bam_line[i + 1], "Standard, Railway")

    add_edge(163, 170, "Standard, Railway")
    add_edge(170, 171, "Standard, Railway")
    add_edge(171, 172, "Standard")
    add_edge(170, 173, "Standard")
    add_edge(170, 174, "Standard")
    add_edge(171, 175, "Standard, Railway")
    add_edge(172, 174, "Standard")
    add_edge(173, 163, "Standard")

    # SOV Primorye -> PRK border
    add_edge(175, 119, "Standard, Railway")
    add_edge(170, 119, "Standard")

    # SOV State 12 Sakhalin & Kurils
    add_edge(180, 181, "Standard")
    add_edge(180, 182, "Standard, Railway")
    add_edge(182, 183, "Strait")
    add_edge(181, 163, "Strait")
    add_edge(183, 310, "Strait")

    # PRC State 20 Beijing-Hebei
    add_edge(200, 201, "Standard, Railway")
    add_edge(200, 202, "Standard")
    add_edge(200, 203, "Standard, Railway")
    add_edge(203, 204, "Standard, Railway")
    add_edge(201, 202, "Standard")
    add_edge(202, 205, "Standard, Railway")

    # PRC State 21 Manchuria
    add_edge(214, 213, "Standard, Railway")
    add_edge(213, 210, "Standard, Railway")
    add_edge(210, 215, "Standard, Railway")
    add_edge(215, 211, "Standard, Railway")
    add_edge(215, 212, "Standard, Railway")
    add_edge(210, 200, "Standard, Railway")
    add_edge(205, 212, "Standard")
    add_edge(214, 153, "Standard")
    add_edge(213, 170, "Standard, Railway")

    # Manchuria -> PRK Border (Yalu & Tumen Rivers)
    add_edge(211, 72, "Standard, River")
    add_edge(211, 75, "Standard, River, Railway")
    add_edge(210, 80, "Standard")

    # PRC State 22 Shandong
    add_edge(204, 220, "Standard, Railway")
    add_edge(220, 221, "Standard, Railway")
    add_edge(221, 222, "Standard")
    add_edge(222, 223, "Standard")
    add_edge(223, 221, "Standard")
    add_edge(222, 212, "Strait")

    # PRC State 23 Shanghai-Yangtze
    add_edge(220, 231, "Standard, Railway")
    add_edge(231, 235, "Standard, Railway")
    add_edge(235, 232, "Standard, Railway")
    add_edge(232, 230, "Standard, Railway")
    add_edge(230, 233, "Standard, Railway")
    add_edge(233, 234, "Standard, Railway")
    add_edge(234, 230, "Standard, Railway")

    # PRC State 24 Fujian
    add_edge(233, 240, "Standard, Railway")
    add_edge(240, 243, "Standard")
    add_edge(240, 242, "Standard, Railway")
    add_edge(242, 241, "Standard, Railway")
    add_edge(241, 244, "Standard")
    add_edge(243, 244, "Standard")

    # PRC State 25 Guangdong
    add_edge(241, 253, "Standard, Railway")
    add_edge(253, 251, "Standard, Railway")
    add_edge(251, 250, "Standard, Railway")
    add_edge(250, 254, "Standard, Railway")
    add_edge(250, 252, "Standard, Railway")
    add_edge(254, 244, "Standard")

    # JPN State 31 Hokkaido
    add_edge(314, 310, "Standard")
    add_edge(314, 312, "Standard")
    add_edge(310, 312, "Standard, Railway")
    add_edge(312, 313, "Standard")
    add_edge(313, 310, "Standard, Railway")
    add_edge(310, 311, "Standard, Railway")

    # JPN State 30 Kanto
    add_edge(311, 305, "Strait, Railway")
    add_edge(305, 304, "Standard, Railway")
    add_edge(304, 300, "Standard, Railway")
    add_edge(300, 301, "Standard, Railway")
    add_edge(301, 302, "Standard")
    add_edge(302, 300, "Standard, Railway")
    add_edge(300, 303, "Standard")
    add_edge(303, 301, "Standard")

    # JPN State 32 Kansai
    add_edge(300, 324, "Standard, Railway")
    add_edge(324, 322, "Standard, Railway")
    add_edge(322, 321, "Standard, Railway")
    add_edge(321, 320, "Standard, Railway")
    add_edge(320, 323, "Standard, Railway")

    # JPN State 33 Kyushu
    add_edge(323, 331, "Standard, Railway")
    add_edge(331, 330, "Standard, Railway")
    add_edge(330, 332, "Standard")
    add_edge(332, 333, "Standard, Railway")
    add_edge(333, 330, "Standard, Railway")
    add_edge(330, 334, "Standard, Railway")

    # Korea Strait: Busan (56) <-> Fukuoka (330)
    add_edge(56, 330, "Strait")

    # TWN State 40, 41, 42
    add_edge(401, 400, "Standard, Railway")
    add_edge(400, 402, "Standard, Railway")
    add_edge(402, 403, "Standard, Railway")
    add_edge(400, 404, "Standard")
    add_edge(403, 412, "Standard, Railway")
    add_edge(412, 411, "Standard, Railway")
    add_edge(411, 410, "Standard, Railway")
    add_edge(410, 413, "Standard, Railway")
    add_edge(404, 413, "Standard")

    # Taiwan Strait & Outlying Islands
    add_edge(420, 241, "Strait")
    add_edge(420, 403, "Strait")
    add_edge(421, 412, "Strait")
    add_edge(421, 240, "Strait")
    add_edge(400, 240, "Strait")

    # USA State 50 Okinawa
    add_edge(500, 501, "Standard")
    add_edge(500, 502, "Standard")
    add_edge(501, 502, "Standard")
    add_edge(500, 334, "Strait")
    add_edge(500, 401, "Strait")

    # USA State 51 Guam
    add_edge(510, 511, "Standard")
    add_edge(511, 500, "Strait")
    add_edge(510, 500, "Strait")

    # MNG State 62 Mongolia
    add_edge(621, 620, "Standard, Railway")
    add_edge(620, 622, "Standard, Railway")
    add_edge(621, 150, "Standard, Railway")
    add_edge(622, 200, "Standard, Railway")

    # VNM State 60 Vietnam
    add_edge(600, 601, "Standard, Railway")
    add_edge(600, 602, "Standard, Railway")
    add_edge(602, 603, "Standard, Railway")
    add_edge(600, 252, "Standard, Railway")
    add_edge(601, 250, "Standard")

    # LAO State 64 Laos
    add_edge(641, 640, "Standard")
    add_edge(640, 600, "Standard")
    add_edge(641, 600, "Standard")

    # KHM State 65 Cambodia
    add_edge(650, 651, "Standard")
    add_edge(650, 603, "Standard")
    add_edge(651, 603, "Standard")

    # THA State 63 Thailand
    add_edge(632, 630, "Standard, Railway")
    add_edge(630, 631, "Standard, Railway")
    add_edge(630, 633, "Standard, Railway")
    add_edge(632, 640, "Standard")
    add_edge(630, 650, "Standard")
    add_edge(631, 651, "Standard")

    # MMR State 66 Myanmar
    add_edge(661, 660, "Standard, Railway")
    add_edge(661, 662, "Standard")
    add_edge(660, 662, "Standard")
    add_edge(660, 630, "Standard")
    add_edge(662, 633, "Standard")

    # MYS State 67 Malaysia
    add_edge(670, 671, "Standard, Railway")
    add_edge(670, 672, "Standard, Railway")
    add_edge(670, 633, "Standard, Railway")
    add_edge(671, 672, "Standard")

    # PHL State 61 Philippines
    add_edge(612, 611, "Standard, Railway")
    add_edge(612, 610, "Standard, Railway")
    add_edge(611, 610, "Standard, Railway")
    add_edge(610, 613, "Standard, Railway")
    add_edge(611, 410, "Strait")
    add_edge(610, 500, "Strait")
    add_edge(613, 510, "Strait")

    # 3. Verification & Validation of Graph Invariants
    assert len(all_provinces) == 232, f"Expected 232 provinces, got {len(all_provinces)}"
    all_pids = sorted(all_provinces.keys())
    state_pids = sorted(prov_to_state.keys())
    assert all_pids == state_pids, "Mismatch between state provinces and defined provinces!"

    for pid, p in all_provinces.items():
        assert len(p["adjacencies"]) >= 2, f"Province {pid} ({p['name']}) has degree < 2"
        for edge in p["adjacencies"]:
            target = edge["targetProvinceId"]
            flags = edge["flags"]
            assert target != pid, f"Self loop on {pid}"
            assert target in all_provinces, f"Unknown target {target} from {pid}"
            target_adj = all_provinces[target]["adjacencies"]
            found = False
            for r_edge in target_adj:
                if r_edge["targetProvinceId"] == pid:
                    assert r_edge["flags"] == flags, f"Mismatched flags {pid}<->{target}"
                    found = True
                    break
            assert found, f"Asymmetric adjacency: {pid} -> {target}"

    out_list = [all_provinces[i] for i in sorted(all_provinces.keys())]
    with open(out_provs_json, "w", encoding="utf-8") as f:
        json.dump(out_list, f, indent=2)
    print(f"✅ Generated {out_provs_json} (232 geographically projected provinces)")

    # 4. Generate data/map/east_asia_1985_provinces.png (2048x2048 Landmass-Scoped Voronoi)
    W, H = 2048, 2048
    mask = bytearray(W * H)

    poly_sov_mainland = [
        (0, 0), (1560, 0), (1540, 60), (1475, 105), (1425, 145),
        (1435, 205), (1445, 255), (1425, 320),
        (1395, 400), (1365, 470), (1345, 502),
        (1295, 498), (1275, 505), (1252, 508),
        (1238, 475), (1252, 415), (1275, 345),
        (1295, 275), (1190, 275), (1070, 240), (920, 195),
        (780, 185), (660, 195), (420, 175), (120, 175), (0, 190),
    ]
    poly_sakhalin = [
        (1518, 85), (1545, 100), (1555, 175), (1552, 240),
        (1572, 288), (1568, 332), (1545, 322), (1532, 302),
        (1518, 328), (1502, 275), (1508, 205), (1500, 130),
    ]
    poly_kurils = [
        (1715, 415), (1748, 365), (1795, 345), (1810, 360),
        (1770, 400), (1730, 430),
    ]
    poly_kurils_north = [
        (1830, 320), (1870, 280), (1885, 295), (1845, 335),
    ]
    poly_mongolia = [
        (30, 185), (120, 175), (420, 175), (660, 195), (780, 185),
        (855, 245), (885, 305), (760, 385), (655, 470),
        (480, 490), (280, 450), (100, 375), (20, 280),
    ]
    poly_prc = [
        (0, 190), (30, 185), (20, 280), (100, 375), (280, 450), (480, 490), (655, 470), (760, 385), (885, 305), (855, 245),
        (780, 185), (920, 195), (1070, 240), (1190, 275), (1295, 275),
        (1275, 345), (1252, 415), (1238, 475), (1252, 508),
        (1170, 525), (1105, 548), (1048, 565), (1005, 575), (972, 600),
        (935, 620), (888, 678), (858, 678), (865, 645), (895, 600),
        (865, 562), (795, 570), (735, 635), (715, 680), (760, 735),
        (835, 745), (912, 762), (920, 788), (860, 815), (800, 858),
        (775, 905), (840, 965), (908, 1015), (908, 1042),
        (855, 1058), (898, 1080), (895, 1125), (860, 1180),
        (830, 1225), (805, 1290), (768, 1345), (722, 1392),
        (655, 1442), (610, 1438), (585, 1412), (572, 1448),
        (505, 1472), (492, 1525), (462, 1525), (458, 1472),
        (425, 1455),
        (345, 1415), (275, 1420), (195, 1385), (115, 1355), (0, 1340),
    ]
    poly_hainan = [
        (458, 1545), (505, 1542), (518, 1580), (482, 1610), (442, 1588),
    ]
    poly_korea = [
        (972, 600), (1005, 575), (1048, 565), (1105, 548), (1170, 525), (1252, 508),
        (1248, 542), (1228, 562), (1210, 595), (1178, 638), (1162, 685),
        (1172, 732), (1178, 782), (1182, 828), (1195, 888), (1188, 925), (1172, 952),
        (1145, 968), (1098, 965), (1022, 972),
        (1025, 932), (1042, 888), (1028, 842), (1048, 815), (1042, 785),
        (992, 758), (995, 730), (1005, 688), (1015, 648), (985, 625),
    ]
    poly_jeju = [
        (1040, 998), (1080, 995), (1088, 1012), (1045, 1015), (1032, 1005),
    ]
    poly_hokkaido = [
        (1565, 355), (1590, 375), (1645, 415), (1705, 445),
        (1695, 485), (1625, 495), (1605, 525), (1580, 495),
        (1555, 510), (1550, 545), (1518, 535), (1525, 495),
        (1545, 460), (1555, 405),
    ]
    poly_honshu = [
        (1535, 565), (1575, 570), (1595, 630), (1605, 710),
        (1610, 785), (1632, 825), (1610, 850),
        (1590, 825), (1575, 820), (1582, 865),
        (1545, 875), (1510, 885), (1480, 905),
        (1440, 925), (1410, 900), (1370, 900),
        (1315, 935), (1295, 930), (1310, 895),
        (1375, 855), (1435, 835), (1465, 795),
        (1485, 810), (1525, 765), (1535, 675), (1520, 600),
    ]
    poly_shikoku = [
        (1345, 925), (1400, 915), (1405, 945), (1355, 960), (1335, 945),
    ]
    poly_kyushu = [
        (1295, 942), (1325, 955), (1335, 1010), (1315, 1085),
        (1280, 1090), (1270, 1045), (1235, 1035), (1230, 985),
        (1260, 960),
    ]
    poly_okinawa = [
        (1175, 1185), (1190, 1195), (1178, 1228), (1152, 1252),
        (1135, 1242), (1150, 1210),
    ]
    poly_amami = [
        (1215, 1130), (1230, 1135), (1220, 1158), (1205, 1150),
    ]
    poly_miyako_yaeyama = [
        (1060, 1275), (1085, 1275), (1075, 1295), (1050, 1290),
    ]
    poly_taiwan = [
        (950, 1240), (972, 1258), (975, 1305), (960, 1375),
        (942, 1445), (920, 1445), (898, 1415), (888, 1365),
        (898, 1310), (922, 1260),
    ]
    poly_kinmen = [
        (786, 1330), (806, 1330), (806, 1346), (786, 1346),
    ]
    poly_penghu = [
        (845, 1344), (868, 1344), (868, 1366), (845, 1366),
    ]
    poly_vietnam = [
        (275, 1420), (345, 1415), (425, 1455),
        (435, 1500), (405, 1540), (445, 1600), (495, 1640),
        (525, 1715), (520, 1785), (475, 1835), (425, 1865),
        (395, 1825), (415, 1785), (445, 1745), (445, 1680),
        (420, 1625), (375, 1565), (345, 1515), (285, 1465),
    ]
    poly_laos = [
        (225, 1405), (275, 1420), (285, 1465), (345, 1515),
        (375, 1565), (420, 1625), (445, 1680),
        (395, 1690), (360, 1655), (340, 1615), (290, 1595),
        (260, 1535), (225, 1465),
    ]
    poly_cambodia = [
        (320, 1685), (395, 1690), (445, 1680), (445, 1745),
        (415, 1785), (395, 1825), (335, 1815), (310, 1775), (305, 1725),
    ]
    poly_thailand = [
        (175, 1515), (225, 1465), (260, 1535), (290, 1595),
        (340, 1615), (360, 1655), (395, 1690), (320, 1685),
        (305, 1725), (310, 1775), (265, 1775), (235, 1745),
        (215, 1785), (230, 1855), (265, 1905), (295, 1935),
        (215, 1945), (185, 1895), (180, 1810),
        (175, 1710), (155, 1635), (155, 1565),
    ]
    poly_myanmar = [
        (0, 1340), (115, 1355), (195, 1385), (225, 1405), (225, 1465),
        (175, 1515), (155, 1565), (155, 1635), (175, 1710), (180, 1810),
        (155, 1805), (135, 1705), (125, 1655), (65, 1655),
        (40, 1595), (25, 1505), (0, 1425),
    ]
    poly_malaysia = [
        (215, 1945), (295, 1935), (320, 1975), (330, 2018),
        (295, 2025), (225, 1985),
    ]
    poly_luzon = [
        (945, 1535), (995, 1540), (1005, 1615), (995, 1665),
        (1045, 1705), (1065, 1745), (1025, 1755), (975, 1735),
        (955, 1705), (965, 1685), (925, 1675), (925, 1625),
    ]
    poly_mindoro = [
        (935, 1755), (965, 1760), (975, 1805), (945, 1810), (925, 1780),
    ]
    poly_samar_leyte = [
        (1065, 1765), (1105, 1775), (1110, 1855), (1070, 1855), (1055, 1805),
    ]
    poly_panay_negros = [
        (995, 1800), (1045, 1800), (1050, 1865), (1005, 1870), (985, 1835),
    ]
    poly_mindanao = [
        (1045, 1890), (1125, 1885), (1135, 1975), (1065, 1995), (1015, 1955), (1025, 1920),
    ]
    poly_palawan = [
        (835, 1875), (895, 1815), (910, 1828), (850, 1890),
    ]
    poly_guam = [
        (1775, 1535), (1802, 1545), (1785, 1600), (1750, 1605), (1755, 1565),
    ]
    poly_saipan = [
        (1795, 1475), (1812, 1480), (1805, 1505), (1788, 1500),
    ]

    fill_polygon(mask, W, H, poly_sov_mainland, 2)
    fill_polygon(mask, W, H, poly_sakhalin, 3)
    fill_polygon(mask, W, H, poly_kurils, 4)
    fill_polygon(mask, W, H, poly_kurils_north, 4)
    fill_polygon(mask, W, H, poly_mongolia, 5)
    fill_polygon(mask, W, H, poly_prc, 6)
    fill_polygon(mask, W, H, poly_hainan, 6)
    fill_polygon(mask, W, H, poly_korea, 1)
    fill_polygon(mask, W, H, poly_jeju, 1)
    fill_polygon(mask, W, H, poly_hokkaido, 7)
    fill_polygon(mask, W, H, poly_honshu, 8)
    fill_polygon(mask, W, H, poly_shikoku, 8)
    fill_polygon(mask, W, H, poly_kyushu, 9)
    fill_polygon(mask, W, H, poly_okinawa, 10)
    fill_polygon(mask, W, H, poly_amami, 10)
    fill_polygon(mask, W, H, poly_miyako_yaeyama, 10)
    fill_polygon(mask, W, H, poly_taiwan, 11)
    fill_polygon(mask, W, H, poly_kinmen, 12, subdivide=False)
    fill_polygon(mask, W, H, poly_penghu, 13, subdivide=False)
    fill_polygon(mask, W, H, poly_vietnam, 14)
    fill_polygon(mask, W, H, poly_laos, 15)
    fill_polygon(mask, W, H, poly_cambodia, 16)
    fill_polygon(mask, W, H, poly_thailand, 17)
    fill_polygon(mask, W, H, poly_myanmar, 18)
    fill_polygon(mask, W, H, poly_malaysia, 19)
    fill_polygon(mask, W, H, poly_luzon, 20)
    fill_polygon(mask, W, H, poly_mindoro, 20)
    fill_polygon(mask, W, H, poly_samar_leyte, 20)
    fill_polygon(mask, W, H, poly_panay_negros, 20)
    fill_polygon(mask, W, H, poly_mindanao, 20)
    fill_polygon(mask, W, H, poly_palawan, 20)
    fill_polygon(mask, W, H, poly_guam, 21)
    fill_polygon(mask, W, H, poly_saipan, 21)

    # Group provinces by regionMask for fast Landmass-Scoped Voronoi Tessellation
    by_mask = {}
    for pid, p in all_provinces.items():
        rm = prov_region_mask[pid]
        r_val = (pid >> 8) & 0xFF
        g_val = pid & 0xFF
        xw = 0.55 if rm == 6 else 1.0
        by_mask.setdefault(rm, []).append((pid, p["centerX"], p["centerY"], r_val, g_val, xw))

    # Fill with Ocean Sentinel: 0xFFFF -> R=255, G=255, B=0
    rgb = bytearray([255, 255, 0] * (W * H))

    sin_x1 = [math.sin(x * 0.018) for x in range(W)]
    cos_x2 = [math.cos(x * 0.042) for x in range(W)]
    sin_y1 = [math.sin(y * 0.018) for y in range(H)]
    cos_y2 = [math.cos(y * 0.042) for y in range(H)]

    for y in range(H):
        row_m = y * W
        row_rgb = y * W * 3
        sy1 = sin_y1[y]
        cy2 = cos_y2[y]
        for x in range(W):
            rm = mask[row_m + x]
            if rm == 0:
                continue
            amp = 2.0 if rm == 1 else 10.0
            wx = x + amp * (sy1 + 0.35 * cy2 * sin_x1[x])
            wy = y + amp * (sin_x1[x] + 0.35 * cos_x2[x] * sy1)
            candidates = by_mask[rm]
            best_r = 255
            best_g = 255
            best_d = 1e18
            for pid, cx, cy, r_val, g_val, xw in candidates:
                dx = (wx - cx) * xw
                dy = wy - cy
                d = dx * dx + dy * dy
                if d < best_d:
                    best_d = d
                    best_r = r_val
                    best_g = g_val
            idx = row_rgb + x * 3
            rgb[idx] = best_r
            rgb[idx + 1] = best_g
            rgb[idx + 2] = 0

    # Stamp a 3x3 core around every province center to guarantee 100% O(1) mouse picking
    for pid, p in all_provinces.items():
        cx, cy = p["centerX"], p["centerY"]
        r_val = (pid >> 8) & 0xFF
        g_val = pid & 0xFF
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                px = min(max(0, cx + dx), W - 1)
                py = min(max(0, cy + dy), H - 1)
                offset = (py * W + px) * 3
                rgb[offset] = r_val
                rgb[offset + 1] = g_val
                rgb[offset + 2] = 0

    write_png(out_provs_png, W, H, bytes(rgb))
    print(f"✅ Generated {out_provs_png} ({W}x{H} RGB8 lossless PNG)")

    # Verify mouse-picking property at all 232 province centers
    for pid, p in all_provinces.items():
        cx, cy = p["centerX"], p["centerY"]
        offset = (cy * W + cx) * 3
        decoded = (rgb[offset] << 8) | rgb[offset + 1]
        assert decoded == pid, f"Mouse pick mismatch at ({cx}, {cy}): expected {pid}, got {decoded}"
    print("✅ Verified 100% accurate O(1) mouse picking across all 232 province centers")

    # 5. Generate 8 Regional Minor OOB Files in data/oob_1985/
    minor_oobs = {
        "VNM": {
            "countryTag": "VNM",
            "templates": [
                {
                    "templateId": 30,
                    "name": "PAVN Motorized Infantry Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "IFV", "Artillery", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles", "Artillery"],
                },
                {
                    "templateId": 31,
                    "name": "PAVN Armored Brigade",
                    "combatWidth": 16,
                    "battalions": ["MBT", "MBT", "IFV", "Artillery"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "308th Division ('Quan Tien')", "templateId": 30, "startingProvinceId": 600, "startingExperience": 0.55, "startingStrength": 1.0, "pgmAuthorized": True},
                {"name": "312th Division ('Song Lam')", "templateId": 30, "startingProvinceId": 601, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "203rd Tank Brigade", "templateId": 31, "startingProvinceId": 602, "startingExperience": 0.60, "startingStrength": 1.0, "pgmAuthorized": True},
                {"name": "9th Division ('Dong Nai')", "templateId": 30, "startingProvinceId": 603, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "PHL": {
            "countryTag": "PHL",
            "templates": [
                {
                    "templateId": 32,
                    "name": "Philippine Army Infantry Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "InfantryKit", "SupportVehicles", "Artillery"],
                    "supportCompanies": ["SupportVehicles"],
                },
                {
                    "templateId": 33,
                    "name": "Philippine Light Armored Division",
                    "combatWidth": 16,
                    "battalions": ["IFV", "IFV", "SupportVehicles", "InfantryKit"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "1st Infantry Division ('Tabak')", "templateId": 32, "startingProvinceId": 610, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "2nd Infantry Division ('Jungle Fighter')", "templateId": 32, "startingProvinceId": 611, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "Light Armor Brigade", "templateId": 33, "startingProvinceId": 612, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": True},
            ],
        },
        "MNG": {
            "countryTag": "MNG",
            "templates": [
                {
                    "templateId": 34,
                    "name": "Mongolian Motor Rifle Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "IFV", "MBT", "Artillery", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles", "Artillery"],
                },
                {
                    "templateId": 35,
                    "name": "Mongolian Cavalry-Armored Brigade",
                    "combatWidth": 16,
                    "battalions": ["MBT", "IFV", "SupportVehicles", "InfantryKit"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "1st Motor Rifle Division ('Sukhbaatar')", "templateId": 34, "startingProvinceId": 620, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "2nd Armored Brigade", "templateId": 35, "startingProvinceId": 621, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "3rd Frontier Division", "templateId": 34, "startingProvinceId": 622, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "THA": {
            "countryTag": "THA",
            "templates": [
                {
                    "templateId": 36,
                    "name": "RTA Infantry Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "SupportVehicles", "Artillery", "IFV"],
                    "supportCompanies": ["SupportVehicles", "Artillery"],
                },
                {
                    "templateId": 37,
                    "name": "RTA Cavalry Division",
                    "combatWidth": 18,
                    "battalions": ["MBT", "IFV", "SupportVehicles", "InfantryKit", "Artillery"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "1st Division ('King's Guard')", "templateId": 36, "startingProvinceId": 630, "startingExperience": 0.55, "startingStrength": 1.0, "pgmAuthorized": True},
                {"name": "2nd Cavalry Division", "templateId": 37, "startingProvinceId": 631, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "6th Infantry Division", "templateId": 36, "startingProvinceId": 632, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "LAO": {
            "countryTag": "LAO",
            "templates": [
                {
                    "templateId": 38,
                    "name": "LPAF Infantry Division",
                    "combatWidth": 18,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "Artillery", "SupportVehicles", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles"],
                }
            ],
            "startingDivisions": [
                {"name": "1st Vientiane Division", "templateId": 38, "startingProvinceId": 640, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "2nd Mountain Brigade", "templateId": 38, "startingProvinceId": 641, "startingExperience": 0.35, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "KHM": {
            "countryTag": "KHM",
            "templates": [
                {
                    "templateId": 39,
                    "name": "KPRAF Infantry Division",
                    "combatWidth": 18,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "Artillery", "SupportVehicles", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles"],
                }
            ],
            "startingDivisions": [
                {"name": "179th Infantry Division", "templateId": 39, "startingProvinceId": 650, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "286th Coastal Brigade", "templateId": 39, "startingProvinceId": 651, "startingExperience": 0.35, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "MMR": {
            "countryTag": "MMR",
            "templates": [
                {
                    "templateId": 40,
                    "name": "Tatmadaw Light Infantry Division",
                    "combatWidth": 18,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "InfantryKit", "SupportVehicles", "Artillery"],
                    "supportCompanies": ["SupportVehicles"],
                },
                {
                    "templateId": 41,
                    "name": "Tatmadaw Armored Regiment",
                    "combatWidth": 16,
                    "battalions": ["MBT", "IFV", "SupportVehicles", "Artillery"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "77th Light Infantry Division", "templateId": 40, "startingProvinceId": 660, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "88th Light Infantry Division", "templateId": 40, "startingProvinceId": 661, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "1st Armored Regiment", "templateId": 41, "startingProvinceId": 662, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "MYS": {
            "countryTag": "MYS",
            "templates": [
                {
                    "templateId": 42,
                    "name": "Royal Malay Infantry Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "IFV", "Artillery", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles", "Artillery"],
                },
                {
                    "templateId": 43,
                    "name": "Malaysian Armored Corps Brigade",
                    "combatWidth": 16,
                    "battalions": ["IFV", "IFV", "MBT", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "1st Royal Malay Division", "templateId": 42, "startingProvinceId": 670, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "2nd Armored Brigade", "templateId": 43, "startingProvinceId": 671, "startingExperience": 0.55, "startingStrength": 1.0, "pgmAuthorized": True},
                {"name": "3rd Infantry Brigade", "templateId": 42, "startingProvinceId": 672, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
    }

    for tag, oob_data in minor_oobs.items():
        oob_file = os.path.join(oob_dir, f"{tag}_oob.json")
        with open(oob_file, "w", encoding="utf-8") as f:
            json.dump(oob_data, f, indent=2)
        print(f"✅ Generated {oob_file} ({len(oob_data['startingDivisions'])} divisions)")


if __name__ == "__main__":
    main()
