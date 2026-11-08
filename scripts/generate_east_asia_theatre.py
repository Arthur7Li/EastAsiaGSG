#!/usr/bin/env python3
"""
generate_east_asia_theatre.py
Generates the scaled 614-province / 41-state East Asian Theatre dataset:
1. data/map/east_asia_1985_provinces.json (614 geographically projected provinces)
2. data/map/east_asia_1985_provinces.png (2048x2048 RGB8 lossless PNG with O(1) picking)
3. data/map/east_asia_1985_states.json (41 states updated with synchronized province lists)
4. data/oob_1985/{VNM, PHL, MNG, THA, LAO, KHM, MMR, MYS}_oob.json (8 Regional Minors)
"""

import json
import math
import os
import struct
import zlib
from collections import defaultdict


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


# Tuple: (pid, name, stateId, ownerTag, terrain, strategicRegionId, hub, port, infra, vp, rmask, cx, cy)
THEATRE_DEFS_PART1 = [
    # State 10: Khabarovsk-Amur BAM Rail Corridor (SOV, Region 1, Mask 2)
    (150, "Irkutsk-Chita Entry Gate", 10, "SOV", "Plains", 1, 5, 0, 8, 25, 2, 160, 95),
    (151, "Taishet-Baikal Junction", 10, "SOV", "Forest", 1, 3, 0, 7, 10, 2, 420, 95),
    (152, "Tynda BAM Central", 10, "SOV", "Forest", 1, 4, 0, 8, 15, 2, 720, 105),
    (153, "Komsomolsk-on-Amur", 10, "SOV", "Urban", 1, 4, 2, 8, 20, 2, 1345, 165),
    (160, "Skovorodino Depot", 10, "SOV", "Plains", 1, 3, 0, 7, 8, 2, 900, 165),
    (161, "Belogorsk Rail Hub", 10, "SOV", "Plains", 1, 3, 0, 7, 10, 2, 1085, 215),
    (162, "Birobidzhan Terminal", 10, "SOV", "Forest", 1, 3, 0, 7, 10, 2, 1220, 265),
    (163, "Khabarovsk Amur Fortress", 10, "SOV", "Urban", 1, 5, 3, 9, 25, 2, 1335, 275),
    (700, "Severobaikalsk Depot", 10, "SOV", "Forest", 1, 2, 0, 6, 5, 2, 320, 95),
    (701, "Chara Iron Basin", 10, "SOV", "Mountain", 1, 2, 0, 6, 5, 2, 560, 100),
    (702, "Aldan Gold Artery", 10, "SOV", "Forest", 1, 2, 0, 6, 5, 2, 810, 95),
    (703, "Neryungri Coal Basin", 10, "SOV", "Hills", 1, 3, 0, 7, 8, 2, 860, 120),
    (704, "Zeysk Hydro Redoubt", 10, "SOV", "Forest", 1, 2, 0, 6, 5, 2, 1000, 135),
    (705, "Chegdomyn Coal Terminal", 10, "SOV", "Forest", 1, 2, 0, 6, 5, 2, 1230, 185),
    (706, "Obluchye Rail Junction", 10, "SOV", "Hills", 1, 2, 0, 7, 5, 2, 1160, 245),
    (707, "Vanino Maritime Anchorage", 10, "SOV", "Hills", 1, 3, 4, 7, 10, 2, 1410, 220),
    (708, "Sovetskaya Gavan Base", 10, "SOV", "Urban", 1, 3, 4, 7, 12, 2, 1420, 260),
    (709, "Magdagachi Transit Hub", 10, "SOV", "Plains", 1, 2, 0, 6, 5, 2, 950, 145),

    # State 11: Primorye-Vladivostok Fleet Bastion (SOV, Region 1, Mask 2)
    (170, "Ussuriysk Armored Redoubt", 11, "SOV", "Plains", 1, 4, 0, 8, 15, 2, 1285, 430),
    (171, "Vladivostok Golden Horn", 11, "SOV", "Urban", 1, 5, 5, 9, 40, 2, 1290, 480),
    (172, "Nakhodka Commercial Port", 11, "SOV", "Hills", 1, 3, 4, 7, 15, 2, 1335, 488),
    (173, "Spassk-Dalny Depot", 11, "SOV", "Forest", 1, 2, 0, 6, 8, 2, 1310, 365),
    (174, "Arsenyev Aviation Works", 11, "SOV", "Hills", 1, 3, 0, 7, 12, 2, 1355, 415),
    (175, "Posyet Bay Frontier", 11, "SOV", "Hills", 1, 3, 3, 7, 10, 2, 1258, 498),
    (710, "Partizansk Coal Basin", 11, "SOV", "Hills", 1, 2, 0, 6, 5, 2, 1320, 455),
    (711, "Dalnegorsk Mining Citadel", 11, "SOV", "Mountain", 1, 2, 0, 6, 5, 2, 1380, 375),
    (712, "Lesozavodsk Timber Terminal", 11, "SOV", "Forest", 1, 2, 0, 6, 5, 2, 1275, 315),
    (713, "Luchegorsk Power Plant", 11, "SOV", "Plains", 1, 2, 0, 6, 5, 2, 1270, 285),
    (714, "Kavalerovo Mountain Pass", 11, "SOV", "Mountain", 1, 2, 0, 6, 4, 2, 1360, 395),
    (715, "Olga Bay Fleet Anchorage", 11, "SOV", "Hills", 1, 2, 3, 6, 6, 2, 1375, 435),
    (716, "Plastun Maritime Haven", 11, "SOV", "Hills", 1, 2, 2, 6, 4, 2, 1390, 345),
    (717, "Slavyanka Submarine Bastion", 11, "SOV", "Hills", 1, 3, 4, 7, 12, 2, 1272, 485),
    (718, "Fokino Closed Fleet Haven", 11, "SOV", "Urban", 1, 3, 4, 7, 12, 2, 1315, 475),
    (719, "Bolshoy Kamen Shipyard", 11, "SOV", "Urban", 1, 3, 4, 8, 12, 2, 1305, 465),
    (720, "Terney Northern Frontier", 11, "SOV", "Forest", 1, 2, 1, 5, 4, 2, 1405, 305),
    (721, "Dalnerechensk Redoubt", 11, "SOV", "Plains", 1, 2, 0, 6, 5, 2, 1260, 300),

    # State 12: Sakhalin (Mask 3) & Kurils (Mask 4) (SOV, Region 2)
    (180, "Yuzhno-Sakhalinsk", 12, "SOV", "Hills", 2, 3, 4, 6, 15, 3, 1545, 270),
    (181, "Kholmsk Strait Ferry", 12, "SOV", "Hills", 2, 2, 4, 6, 10, 3, 1518, 240),
    (182, "Korsakov Naval Terminal", 12, "SOV", "Plains", 2, 2, 3, 6, 10, 3, 1552, 312),
    (183, "Iturup Kuril Bastion", 12, "SOV", "Mountain", 2, 2, 3, 5, 12, 4, 1755, 385),
    (722, "Okha Oil Basin", 12, "SOV", "Plains", 2, 2, 2, 5, 8, 3, 1530, 115),
    (723, "Nogliki Railhead", 12, "SOV", "Forest", 2, 2, 0, 5, 5, 3, 1540, 150),
    (724, "Poronaysk Maritime Pier", 12, "SOV", "Plains", 2, 2, 2, 6, 5, 3, 1545, 215),
    (725, "Makarov Coastal Gate", 12, "SOV", "Hills", 2, 2, 1, 5, 4, 3, 1550, 245),
    (726, "Nevelsk Southwest Harbor", 12, "SOV", "Hills", 2, 2, 2, 5, 5, 3, 1515, 280),
    (727, "Kunashir Kuril Post", 12, "SOV", "Mountain", 2, 2, 2, 5, 6, 4, 1730, 405),
    (728, "Paramushir North Kurils", 12, "SOV", "Mountain", 2, 1, 2, 4, 5, 4, 1860, 305),
    (729, "Urup Central Kuril Bastion", 12, "SOV", "Mountain", 2, 1, 2, 4, 5, 4, 1785, 365),

    # State 20: Beijing-Tianjin Direct Municipality (PRC, Region 3, Mask 6)
    (200, "Beijing Central Citadel", 20, "PRC", "Urban", 3, 5, 0, 9, 50, 6, 585, 585),
    (201, "Tianjin Binhai Port", 20, "PRC", "Urban", 3, 4, 4, 8, 25, 6, 685, 655),
    (730, "Haidian Science District", 20, "PRC", "Urban", 3, 3, 0, 8, 10, 6, 565, 575),
    (731, "Tongzhou Eastern Canal Gate", 20, "PRC", "Plains", 3, 3, 0, 8, 8, 6, 615, 590),
    (732, "Daxing Southern Airfield", 20, "PRC", "Plains", 3, 3, 0, 8, 8, 6, 590, 615),
    (733, "Changping Juyong Pass", 20, "PRC", "Hills", 3, 2, 0, 7, 6, 6, 560, 555),
    (734, "Miyun Reservoir Bastion", 20, "PRC", "Mountain", 3, 2, 0, 6, 5, 6, 610, 550),
    (735, "Wuqing Industrial Corridor", 20, "PRC", "Plains", 3, 3, 0, 8, 6, 6, 635, 625),
    (736, "Tanggu Deepwater Anchorage", 20, "PRC", "Urban", 3, 4, 4, 8, 12, 6, 705, 665),
    (737, "Dagang Oilfield Basin", 20, "PRC", "Plains", 3, 3, 2, 7, 8, 6, 685, 685),

    # State 21: Hebei Province (PRC, Region 3, Mask 6)
    (202, "Tangshan Industrial Basin", 21, "PRC", "Plains", 3, 3, 0, 7, 15, 6, 725, 595),
    (203, "Baoding Southern Gate", 21, "PRC", "Plains", 3, 3, 0, 7, 10, 6, 420, 665),
    (204, "Shijiazhuang Rail Hub", 21, "PRC", "Plains", 3, 4, 0, 8, 15, 6, 220, 725),
    (205, "Qinhuangdao Naval Gate", 21, "PRC", "Hills", 3, 3, 3, 7, 12, 6, 805, 555),
    (740, "Handan Ironworks Citadel", 21, "PRC", "Urban", 3, 3, 0, 7, 10, 6, 260, 785),
    (741, "Xingtai Mining Basin", 21, "PRC", "Plains", 3, 2, 0, 7, 6, 6, 250, 755),
    (742, "Cangzhou Grand Canal Gate", 21, "PRC", "Plains", 3, 3, 0, 7, 8, 6, 620, 705),
    (743, "Hengshui Plain Junction", 21, "PRC", "Plains", 3, 2, 0, 7, 6, 6, 410, 720),
    (744, "Langfang Transit Corridor", 21, "PRC", "Plains", 3, 2, 0, 7, 6, 6, 595, 625),
    (745, "Zhangjiakou Northern Gate", 21, "PRC", "Hills", 3, 3, 0, 7, 10, 6, 470, 535),
    (746, "Chengde Mountain Redoubt", 21, "PRC", "Mountain", 3, 2, 0, 6, 8, 6, 670, 535),
    (747, "Taihang Mountain Pass", 21, "PRC", "Mountain", 3, 2, 0, 6, 5, 6, 200, 690),
    (748, "Zunhua Northern Arsenal", 21, "PRC", "Hills", 3, 2, 0, 6, 5, 6, 720, 560),
    (749, "Renqiu Huabei Oilfield", 21, "PRC", "Plains", 3, 3, 0, 7, 10, 6, 510, 675),
    (750, "Dingzhou Armored Staging Hub", 21, "PRC", "Plains", 3, 2, 0, 7, 6, 6, 330, 695),
    (751, "Botou Grand Canal Depot", 21, "PRC", "Plains", 3, 2, 0, 6, 5, 6, 580, 730),

    # State 22: Liaoning Province & Dalian (PRC, Region 3, Mask 6)
    (210, "Shenyang Arsenal Hub", 22, "PRC", "Urban", 3, 5, 0, 8, 25, 6, 935, 525),
    (211, "Dandong Yalu Crossing", 22, "PRC", "Plains", 3, 4, 2, 7, 15, 6, 952, 595),
    (212, "Dalian Lushun Fleet Port", 22, "PRC", "Urban", 3, 4, 5, 8, 25, 6, 875, 660),
    (215, "Anshan Steelworks", 22, "PRC", "Plains", 3, 3, 0, 7, 15, 6, 910, 572),
    (752, "Jinzhou Coastal Passage", 22, "PRC", "Plains", 3, 3, 2, 7, 10, 6, 835, 565),
    (753, "Huludao Naval Shipyards", 22, "PRC", "Urban", 3, 3, 4, 8, 15, 6, 825, 585),
    (754, "Panjin Liaohe Oil Basin", 22, "PRC", "Plains", 3, 3, 2, 7, 10, 6, 875, 565),
    (755, "Yingkou Bohai Terminal", 22, "PRC", "Plains", 3, 3, 3, 7, 10, 6, 890, 595),
    (756, "Benxi Iron Mountain", 22, "PRC", "Mountain", 3, 3, 0, 7, 10, 6, 960, 545),
    (757, "Fushun Coal Basin Citadel", 22, "PRC", "Urban", 3, 3, 0, 7, 10, 6, 975, 515),
    (758, "Liaoyang Ancient Arsenal", 22, "PRC", "Plains", 3, 2, 0, 7, 6, 6, 925, 550),
    (759, "Fuxing Rail Junction", 22, "PRC", "Plains", 3, 2, 0, 6, 5, 6, 860, 520),
    (760, "Chaoyang Western Redoubt", 22, "PRC", "Hills", 3, 2, 0, 6, 5, 6, 805, 535),
    (761, "Wafangdian Corridor", 22, "PRC", "Plains", 3, 2, 1, 7, 6, 6, 885, 625),
    (762, "Zhuanghe Yellow Sea Coast", 22, "PRC", "Plains", 3, 2, 2, 6, 6, 6, 930, 625),
    (763, "Tieling Northern Staging", 22, "PRC", "Plains", 3, 2, 0, 6, 5, 6, 965, 485),

    # State 23: Jilin & Heilongjiang Frontier (PRC, Region 3, Mask 6)
    (213, "Changchun Rail Center", 23, "PRC", "Plains", 3, 4, 0, 7, 20, 6, 1010, 445),
    (214, "Harbin Northern Fortress", 23, "PRC", "Urban", 3, 4, 1, 8, 25, 6, 1065, 355),
    (764, "Jilin Songhua Hydro Basin", 23, "PRC", "Hills", 3, 3, 0, 7, 12, 6, 1045, 470),
    (765, "Siping Rail Crossroads", 23, "PRC", "Plains", 3, 3, 0, 7, 8, 6, 980, 475),
    (766, "Tonghua Mountain Frontier", 23, "PRC", "Mountain", 3, 2, 0, 6, 6, 6, 1010, 535),
    (767, "Baishan Yalu Highlands", 23, "PRC", "Mountain", 3, 2, 0, 6, 6, 6, 1060, 525),
    (768, "Yanji Tumen Frontier Basin", 23, "PRC", "Hills", 3, 3, 0, 7, 10, 6, 1140, 495),
    (769, "Daqing Petrochemical Titan", 23, "PRC", "Urban", 3, 4, 0, 8, 20, 6, 1015, 335),
    (770, "Qiqihar Western Armor Works", 23, "PRC", "Urban", 3, 3, 0, 7, 12, 6, 965, 320),
    (771, "Mudanjiang Eastern Fortress", 23, "PRC", "Urban", 3, 3, 0, 7, 12, 6, 1150, 395),
    (772, "Jiamusi Songhua River Port", 23, "PRC", "Plains", 3, 3, 2, 7, 10, 6, 1165, 320),
    (773, "Hegang Coal Basin", 23, "PRC", "Hills", 3, 2, 0, 6, 6, 6, 1155, 285),
    (774, "Shuangyashan Arsenal", 23, "PRC", "Plains", 3, 2, 0, 6, 6, 6, 1205, 325),
    (775, "Suihua Central Granary", 23, "PRC", "Plains", 3, 2, 0, 6, 5, 6, 1075, 315),
    (776, "Heihe Amur Border Gate", 23, "PRC", "Plains", 3, 3, 1, 6, 10, 6, 1060, 215),
    (777, "Mohe Northernmost Post", 23, "PRC", "Forest", 3, 1, 0, 5, 5, 6, 880, 155),
    (778, "Hunchun Tumen Delta Gate", 23, "PRC", "Plains", 3, 3, 2, 7, 10, 6, 1195, 505),
    (779, "Suifenhe Border Terminal", 23, "PRC", "Hills", 3, 3, 0, 7, 10, 6, 1215, 425),

    # State 24: Shandong Peninsula & Qingdao (PRC, Region 4, Mask 6)
    (220, "Jinan Yellow River Gate", 24, "PRC", "Urban", 4, 4, 0, 8, 20, 6, 545, 805),
    (221, "Qingdao North Sea Bastion", 24, "PRC", "Urban", 4, 5, 5, 9, 25, 6, 805, 835),
    (222, "Yantai Bohai Strait Port", 24, "PRC", "Hills", 4, 3, 4, 7, 15, 6, 845, 765),
    (223, "Weihai Fleet Anchorage", 24, "PRC", "Hills", 4, 3, 3, 7, 15, 6, 895, 775),
    (780, "Zibo Petrochemical Hub", 24, "PRC", "Urban", 4, 3, 0, 7, 10, 6, 615, 805),
    (781, "Weifang Central Plain Gate", 24, "PRC", "Plains", 4, 3, 0, 7, 8, 6, 705, 805),
    (782, "Dongying Shengli Oilfield", 24, "PRC", "Plains", 4, 4, 2, 8, 15, 6, 670, 765),
    (783, "Tai'an Taishan Bastion", 24, "PRC", "Mountain", 4, 2, 0, 7, 6, 6, 545, 835),
    (784, "Jining Grand Canal Core", 24, "PRC", "Plains", 4, 3, 0, 7, 8, 6, 475, 865),
    (785, "Linyi Southern Pass", 24, "PRC", "Hills", 4, 2, 0, 6, 6, 6, 645, 875),
    (786, "Rizhao Maritime Lock", 24, "PRC", "Plains", 4, 3, 3, 7, 10, 6, 745, 875),
    (787, "Dezhou Yellow River Pass", 24, "PRC", "Plains", 4, 2, 0, 7, 6, 6, 460, 775),
    (788, "Liaocheng Western Redoubt", 24, "PRC", "Plains", 4, 2, 0, 6, 5, 6, 410, 815),
    (789, "Heze Southernmost Artery", 24, "PRC", "Plains", 4, 2, 0, 6, 5, 6, 375, 865),
    (790, "Zaozhuang Coal Basin", 24, "PRC", "Hills", 4, 2, 0, 6, 6, 6, 540, 895),
    (791, "Penglai Bohai Watchpost", 24, "PRC", "Hills", 4, 2, 2, 6, 6, 6, 795, 760),

    # State 25: Jiangsu & Shanghai Megalopolis (PRC, Region 4, Mask 6)
    (230, "Shanghai Huangpu Core", 25, "PRC", "Urban", 4, 5, 5, 10, 35, 6, 890, 1025),
    (231, "Nanjing Yangtze Citadel", 25, "PRC", "Urban", 4, 4, 2, 8, 20, 6, 520, 985),
    (232, "Suzhou Industrial Park", 25, "PRC", "Plains", 4, 3, 0, 8, 15, 6, 815, 1015),
    (235, "Wuxi Grand Canal Basin", 25, "PRC", "Plains", 4, 3, 0, 7, 12, 6, 695, 995),
    (792, "Changzhou Industrial Basin", 25, "PRC", "Plains", 4, 3, 0, 7, 10, 6, 640, 995),
    (793, "Zhenjiang Yangtze Crossing", 25, "PRC", "Urban", 4, 3, 2, 8, 10, 6, 580, 985),
    (794, "Nantong North Delta Port", 25, "PRC", "Plains", 4, 3, 4, 8, 12, 6, 825, 975),
    (795, "Yangzhou Grand Canal Port", 25, "PRC", "Urban", 4, 3, 2, 7, 10, 6, 580, 955),
    (796, "Taizhou Central Plain Gate", 25, "PRC", "Plains", 4, 2, 0, 7, 6, 6, 660, 955),
    (797, "Yancheng Coastal Flats", 25, "PRC", "Plains", 4, 2, 2, 6, 6, 6, 735, 925),
    (798, "Huai'an Transport Hub", 25, "PRC", "Plains", 4, 3, 0, 7, 8, 6, 580, 915),
    (799, "Lianyungang Eurasian Gate", 25, "PRC", "Urban", 4, 4, 4, 8, 15, 6, 695, 875),
    (800, "Xuzhou Strategic Crossroads", 25, "PRC", "Urban", 4, 4, 0, 8, 15, 6, 450, 895),
    (801, "Suqian Canal Redoubt", 25, "PRC", "Plains", 4, 2, 0, 6, 5, 6, 520, 905),
    (802, "Baoshan Steel Titan", 25, "PRC", "Urban", 4, 4, 4, 9, 15, 6, 870, 1005),
    (803, "Pudong Maritime Reserve", 25, "PRC", "Plains", 4, 3, 3, 8, 10, 6, 905, 1035),
    (804, "Songjiang Transit Hub", 25, "PRC", "Plains", 4, 2, 0, 7, 6, 6, 855, 1045),
    (805, "Chongming Estuary Island", 25, "PRC", "Plains", 4, 2, 2, 6, 6, 6, 885, 995),

    # State 26: Zhejiang Province & Ningbo (PRC, Region 4, Mask 6)
    (233, "Hangzhou Qiantang Hub", 26, "PRC", "Urban", 4, 4, 2, 8, 20, 6, 745, 1085),
    (234, "Ningbo Zhoushan Anchorage", 26, "PRC", "Hills", 4, 4, 5, 8, 25, 6, 870, 1100),
    (806, "Shaoxing Textile Heartland", 26, "PRC", "Plains", 4, 3, 0, 7, 8, 6, 775, 1105),
    (807, "Jiaxing Grand Canal Gate", 26, "PRC", "Plains", 4, 3, 0, 7, 8, 6, 795, 1065),
    (808, "Huzhou South Taihu Port", 26, "PRC", "Plains", 4, 2, 1, 7, 6, 6, 720, 1065),
    (809, "Jinhua Central Basin", 26, "PRC", "Hills", 4, 3, 0, 7, 8, 6, 705, 1145),
    (810, "Quzhou Western Pass", 26, "PRC", "Mountain", 4, 2, 0, 6, 5, 6, 615, 1155),
    (811, "Zhoushan Archipelago Bastion", 26, "PRC", "Hills", 4, 4, 5, 8, 15, 6, 905, 1095),
    (812, "Taizhou Maritime Harbor", 26, "PRC", "Hills", 4, 3, 3, 7, 10, 6, 825, 1165),
    (813, "Wenzhou Coastal Bastion", 26, "PRC", "Urban", 4, 4, 4, 8, 15, 6, 785, 1215),
    (814, "Lishui Mountain Arsenal", 26, "PRC", "Mountain", 4, 2, 0, 6, 5, 6, 695, 1195),
    (815, "Yiwu Inland Trade Depot", 26, "PRC", "Plains", 4, 3, 0, 7, 8, 6, 740, 1135),
    (816, "Cixi Coastal Redoubt", 26, "PRC", "Plains", 4, 2, 1, 7, 6, 6, 835, 1085),
    (817, "Yuyao Shanjia Crossing", 26, "PRC", "Plains", 4, 2, 0, 7, 5, 6, 815, 1100),

    # State 27: Fujian Strait Frontline (PRC, Region 5, Mask 6)
    (240, "Fuzhou Strait Command", 27, "PRC", "Urban", 5, 4, 4, 8, 25, 6, 810, 1225),
    (241, "Xiamen Frontline Redoubt", 27, "PRC", "Urban", 5, 4, 4, 8, 20, 6, 755, 1330),
    (242, "Quanzhou Maritime Hub", 27, "PRC", "Plains", 5, 3, 3, 7, 15, 6, 785, 1285),
    (243, "Nanping Mountain Arsenal", 27, "PRC", "Mountain", 5, 2, 0, 6, 8, 6, 565, 1185),
    (244, "Longyan Southern Highlands", 27, "PRC", "Mountain", 5, 2, 0, 6, 8, 6, 540, 1265),
    (818, "Zhangzhou Strait Port", 27, "PRC", "Urban", 5, 3, 3, 7, 10, 6, 730, 1350),
    (819, "Putian Mazu Coast", 27, "PRC", "Plains", 5, 3, 2, 7, 8, 6, 795, 1255),
    (820, "Sanming Industrial Core", 27, "PRC", "Mountain", 5, 2, 0, 6, 6, 6, 585, 1225),
    (821, "Ningde Sanduao Anchorage", 27, "PRC", "Hills", 5, 3, 4, 7, 12, 6, 815, 1185),
    (822, "Wuyishan Strategic Pass", 27, "PRC", "Mountain", 5, 2, 0, 6, 5, 6, 520, 1155),
    (823, "Yong'an Inland Redoubt", 27, "PRC", "Hills", 5, 2, 0, 6, 5, 6, 610, 1260),
    (824, "Fu'an Coastal Arsenal", 27, "PRC", "Hills", 5, 2, 2, 6, 5, 6, 785, 1165),
    (825, "Changle Strait Airfield", 27, "PRC", "Plains", 5, 3, 0, 8, 8, 6, 825, 1230),
    (826, "Jinjiang Maritime Redoubt", 27, "PRC", "Plains", 5, 2, 2, 7, 6, 6, 775, 1305),
    (827, "Nan'an Shijing Terminal", 27, "PRC", "Plains", 5, 2, 1, 7, 5, 6, 755, 1300),
    (828, "Tong'an Frontline Sector", 27, "PRC", "Plains", 5, 2, 0, 7, 5, 6, 745, 1320),
    (829, "Haicang Port Terminal", 27, "PRC", "Urban", 5, 3, 3, 8, 8, 6, 745, 1340),
    (830, "Dongshan Island Bastion", 27, "PRC", "Hills", 5, 3, 3, 7, 8, 6, 715, 1380),
    (831, "Pingtan Strait Vanguard", 27, "PRC", "Hills", 5, 3, 3, 7, 10, 6, 835, 1250),

    # State 28: Guangdong & Shenzhen SEZ (PRC, Region 5, Mask 6)
    (250, "Guangzhou Pearl Delta Core", 28, "PRC", "Urban", 5, 5, 4, 9, 30, 6, 545, 1390),
    (251, "Shenzhen Reform Corridor", 28, "PRC", "Urban", 5, 4, 4, 9, 20, 6, 625, 1425),
    (253, "Shantou Eastern Port", 28, "PRC", "Plains", 5, 3, 3, 7, 12, 6, 705, 1380),
    (254, "Shaoguan Northern Pass", 28, "PRC", "Mountain", 5, 3, 0, 6, 10, 6, 360, 1285),
    (832, "Dongguan Industrial Artery", 28, "PRC", "Urban", 5, 3, 0, 8, 12, 6, 585, 1410),
    (833, "Foshan Nanhai Arsenal", 28, "PRC", "Urban", 5, 3, 0, 8, 12, 6, 515, 1395),
    (834, "Zhongshan Delta Port", 28, "PRC", "Plains", 5, 3, 2, 8, 8, 6, 550, 1435),
    (835, "Zhuhai Special Zone", 28, "PRC", "Urban", 5, 3, 3, 8, 10, 6, 565, 1450),
    (836, "Jiangmen Wuyi Basin", 28, "PRC", "Plains", 5, 2, 1, 7, 6, 6, 495, 1435),
    (837, "Huizhou Daya Bay Citadel", 28, "PRC", "Hills", 5, 3, 3, 8, 10, 6, 645, 1415),
    (838, "Chaozhou Han River Gate", 28, "PRC", "Urban", 5, 2, 0, 7, 6, 6, 725, 1365),
    (839, "Jieyang Hinterland", 28, "PRC", "Plains", 5, 2, 0, 6, 5, 6, 685, 1375),
    (840, "Shanwei Coastal Haven", 28, "PRC", "Plains", 5, 2, 2, 6, 6, 6, 675, 1415),
    (841, "Heyuan Dongjiang Basin", 28, "PRC", "Hills", 5, 2, 0, 6, 5, 6, 615, 1365),
    (842, "Meizhou Hakka Mountain", 28, "PRC", "Mountain", 5, 2, 0, 6, 6, 6, 695, 1325),
    (843, "Qingyuan Delta Gate", 28, "PRC", "Hills", 5, 2, 0, 6, 5, 6, 515, 1355),
    (844, "Zhaoqing Xijiang Waterway", 28, "PRC", "Plains", 5, 2, 1, 7, 6, 6, 455, 1395),
    (845, "Yunfu Mountain Pass", 28, "PRC", "Mountain", 5, 2, 0, 6, 5, 6, 415, 1415),
    (846, "Yangjiang Coastal Redoubt", 28, "PRC", "Plains", 5, 2, 2, 6, 6, 6, 435, 1465),
    (847, "Maoming Petrochemical Core", 28, "PRC", "Urban", 5, 3, 3, 7, 10, 6, 415, 1485),

    # State 29: Hainan & Leizhou South Sea Bastion (PRC, Region 5, Mask 6)
    (252, "Zhanjiang South Sea Bastion", 29, "PRC", "Plains", 5, 4, 5, 8, 20, 6, 475, 1480),
    (848, "Haikou Qiongzhou Gate", 29, "PRC", "Urban", 5, 3, 4, 8, 15, 6, 485, 1545),
    (849, "Sanya Yulin Fleet Bastion", 29, "PRC", "Urban", 5, 4, 5, 8, 25, 6, 485, 1610),
    (850, "Danzhou Northwest Port", 29, "PRC", "Plains", 5, 2, 3, 7, 8, 6, 450, 1565),
    (851, "Wenchang Eastern Cape", 29, "PRC", "Plains", 5, 2, 2, 7, 8, 6, 515, 1560),
    (852, "Qionghai Wanquan Basin", 29, "PRC", "Plains", 5, 2, 1, 6, 6, 6, 515, 1585),
    (853, "Wanning Southeast Shore", 29, "PRC", "Plains", 5, 2, 2, 6, 5, 6, 505, 1600),
    (854, "Dongfang Western Pier", 29, "PRC", "Plains", 5, 2, 2, 6, 6, 6, 445, 1590),
    (855, "Leizhou Peninsula Spine", 29, "PRC", "Plains", 5, 2, 0, 7, 6, 6, 470, 1510),
]

THEATRE_DEFS_PART2 = [
    # State 30: Tokyo-Kanto Economic Megalopolis (JPN, Region 6, Mask 8)
    (300, "Tokyo Chiyoda Citadel", 30, "JPN", "Urban", 6, 5, 4, 10, 50, 8, 1578, 808),
    (301, "Yokohama Maritime Hub", 30, "JPN", "Urban", 6, 5, 5, 10, 20, 8, 1562, 835),
    (302, "Yokosuka USN Fleet Base", 30, "JPN", "Urban", 6, 4, 5, 9, 20, 8, 1574, 856),
    (303, "Chiba Boso Peninsula", 30, "JPN", "Plains", 6, 3, 3, 8, 10, 8, 1612, 822),
    (304, "Saitama Northern Transit", 30, "JPN", "Urban", 6, 4, 0, 8, 12, 8, 1560, 778),
    (305, "Utsunomiya North Gate", 30, "JPN", "Hills", 6, 3, 0, 7, 10, 8, 1575, 725),
    (860, "Tachikawa Air Base Corridor", 30, "JPN", "Urban", 6, 3, 0, 8, 8, 8, 1550, 805),
    (861, "Hachioji Western Gateway", 30, "JPN", "Hills", 6, 3, 0, 8, 8, 8, 1535, 815),
    (862, "Machida Transit Hub", 30, "JPN", "Urban", 6, 3, 0, 8, 6, 8, 1545, 825),
    (863, "Kawasaki Industrial Belt", 30, "JPN", "Urban", 6, 4, 3, 9, 12, 8, 1570, 820),
    (864, "Sagamihara Logistic Base", 30, "JPN", "Plains", 6, 3, 0, 8, 8, 8, 1540, 835),
    (865, "Omiya Northern Rail Nexus", 30, "JPN", "Urban", 6, 4, 0, 9, 10, 8, 1565, 770),
    (866, "Kawagoe Historical Hub", 30, "JPN", "Plains", 6, 3, 0, 7, 6, 8, 1550, 770),
    (867, "Kashiwa Joban Corridor", 30, "JPN", "Plains", 6, 3, 0, 8, 6, 8, 1585, 785),
    (868, "Funabashi Bay Harbor", 30, "JPN", "Plains", 6, 3, 2, 8, 8, 8, 1590, 810),
    (869, "Kisarazu Tokyo Bay Gate", 30, "JPN", "Plains", 6, 3, 2, 8, 6, 8, 1600, 835),
    (870, "Mito Ibaraki Command", 30, "JPN", "Urban", 6, 3, 1, 7, 10, 8, 1610, 745),
    (871, "Takasaki Pass", 30, "JPN", "Hills", 6, 3, 0, 7, 8, 8, 1530, 745),

    # State 31: Hokkaido Northern Defense Frontier (JPN, Region 6, Mask 7)
    (310, "Sapporo Northern Command", 31, "JPN", "Urban", 6, 4, 2, 8, 25, 7, 1565, 470),
    (311, "Hakodate Tsugaru Port", 31, "JPN", "Hills", 6, 3, 4, 7, 15, 7, 1538, 528),
    (312, "Asahikawa Armored Center", 31, "JPN", "Plains", 6, 3, 0, 7, 12, 7, 1602, 435),
    (313, "Kushiro Pacific Gateway", 31, "JPN", "Plains", 6, 3, 3, 6, 10, 7, 1670, 465),
    (314, "Wakkanai Soya Watchpost", 31, "JPN", "Hills", 6, 2, 3, 6, 10, 7, 1572, 380),
    (872, "Otaru Sea of Japan Port", 31, "JPN", "Urban", 6, 3, 3, 7, 10, 7, 1545, 465),
    (873, "Tomakomai Industrial Port", 31, "JPN", "Urban", 6, 3, 3, 8, 10, 7, 1575, 495),
    (874, "Muroran Steel Bastion", 31, "JPN", "Urban", 6, 3, 3, 7, 10, 7, 1555, 505),
    (875, "Obihiro Tokachi Plain Hub", 31, "JPN", "Plains", 6, 2, 0, 7, 8, 7, 1635, 465),
    (876, "Kitami Okhotsk Artery", 31, "JPN", "Hills", 6, 2, 0, 6, 6, 7, 1645, 425),
    (877, "Abashiri Okhotsk Watchpost", 31, "JPN", "Hills", 6, 2, 2, 6, 6, 7, 1675, 425),
    (878, "Nemuro Cape Frontier", 31, "JPN", "Plains", 6, 2, 2, 6, 8, 7, 1700, 450),
    (879, "Rumoi Northern Coast", 31, "JPN", "Hills", 6, 2, 1, 6, 5, 7, 1560, 430),
    (880, "Nayoro Staging Post", 31, "JPN", "Plains", 6, 2, 0, 6, 5, 7, 1585, 405),
    (881, "Chitose Air Base", 31, "JPN", "Plains", 6, 3, 0, 8, 12, 7, 1575, 480),
    (882, "Iwamizawa Sorachi Basin", 31, "JPN", "Plains", 6, 2, 0, 7, 6, 7, 1575, 460),
    (883, "Furano Central Highlands", 31, "JPN", "Mountain", 6, 2, 0, 6, 5, 7, 1605, 450),

    # State 32: Kansai-Chubu Industrial Heartland (JPN, Region 7, Mask 8)
    (320, "Osaka Bay Metropolis", 32, "JPN", "Urban", 7, 5, 4, 9, 30, 8, 1412, 885),
    (321, "Kyoto Cultural Core", 32, "JPN", "Urban", 7, 3, 0, 8, 15, 8, 1422, 855),
    (322, "Nagoya Tokai Arsenal", 32, "JPN", "Urban", 7, 4, 3, 9, 25, 8, 1472, 858),
    (323, "Kobe Port Terminal", 32, "JPN", "Urban", 7, 4, 4, 9, 15, 8, 1375, 885),
    (324, "Shizuoka Tokaido Route", 32, "JPN", "Hills", 7, 3, 2, 8, 10, 8, 1522, 865),
    (884, "Sakai Industrial Shore", 32, "JPN", "Urban", 7, 4, 3, 9, 10, 8, 1410, 900),
    (885, "Nara Ancient Redoubt", 32, "JPN", "Hills", 7, 2, 0, 8, 8, 8, 1430, 890),
    (886, "Otsu Lake Biwa Lock", 32, "JPN", "Plains", 7, 3, 1, 8, 8, 8, 1435, 860),
    (887, "Yokkaichi Petrochemical Hub", 32, "JPN", "Urban", 7, 3, 3, 8, 10, 8, 1455, 875),
    (888, "Toyohashi Mikawa Port", 32, "JPN", "Plains", 7, 3, 2, 8, 8, 8, 1490, 875),
    (889, "Hamamatsu Industrial Core", 32, "JPN", "Urban", 7, 3, 0, 8, 10, 8, 1505, 870),
    (890, "Numazu Suruga Anchorage", 32, "JPN", "Hills", 7, 2, 2, 7, 6, 8, 1540, 860),
    (891, "Gifu Armor & Aero Works", 32, "JPN", "Urban", 7, 3, 0, 8, 10, 8, 1465, 840),
    (892, "Himeji Castle Gateway", 32, "JPN", "Urban", 7, 3, 2, 8, 10, 8, 1350, 885),
    (893, "Wakayama Kii Bastion", 32, "JPN", "Hills", 7, 2, 2, 7, 6, 8, 1405, 920),
    (894, "Fukui Hokuriku Corridor", 32, "JPN", "Hills", 7, 2, 1, 7, 6, 8, 1435, 815),
    (895, "Kanazawa Ishikawa Fortress", 32, "JPN", "Urban", 7, 3, 2, 7, 10, 8, 1465, 785),

    # State 33: Kyushu-Sasebo Western Maritime Gate (JPN, Region 7, Mask 9)
    (330, "Fukuoka Tsushima Gate", 33, "JPN", "Urban", 7, 4, 4, 9, 25, 9, 1275, 975),
    (331, "Kitakyushu Ironworks", 33, "JPN", "Urban", 7, 4, 3, 8, 15, 9, 1300, 955),
    (332, "Sasebo Naval Arsenal", 33, "JPN", "Hills", 7, 4, 5, 8, 20, 9, 1245, 1000),
    (333, "Nagasaki Shipyards", 33, "JPN", "Hills", 7, 3, 4, 8, 15, 9, 1255, 1030),
    (334, "Kagoshima South Cape", 33, "JPN", "Mountain", 7, 3, 3, 7, 15, 9, 1295, 1072),
    (896, "Kurume Chikugo Basin", 33, "JPN", "Plains", 7, 3, 0, 8, 8, 9, 1280, 990),
    (897, "Kumamoto Central Citadel", 33, "JPN", "Urban", 7, 3, 0, 8, 12, 9, 1285, 1020),
    (898, "Oita Bungo Strait Port", 33, "JPN", "Hills", 7, 3, 2, 7, 8, 9, 1320, 995),
    (899, "Beppu Geothermal Basin", 33, "JPN", "Hills", 7, 2, 1, 7, 6, 9, 1315, 980),
    (900, "Miyazaki Hyuga Coast", 33, "JPN", "Plains", 7, 2, 2, 7, 8, 9, 1320, 1055),
    (901, "Miyakonojo Kirishima Pass", 33, "JPN", "Mountain", 7, 2, 0, 6, 5, 9, 1305, 1065),
    (902, "Kanoya Naval Airbase", 33, "JPN", "Plains", 7, 3, 0, 8, 10, 9, 1295, 1085),
    (903, "Yatsushiro Shiranui Port", 33, "JPN", "Plains", 7, 2, 2, 7, 6, 9, 1280, 1035),
    (904, "Omuta Ariake Shore", 33, "JPN", "Plains", 7, 2, 1, 7, 6, 9, 1270, 1005),
    (905, "Tsushima Island Strategic Lock", 33, "JPN", "Mountain", 7, 2, 3, 6, 12, 9, 1245, 935),

    # State 40: Taipei-Hsinchu Silicon Corridor (TWN, Region 8, Mask 11)
    (400, "Taipei Capital District", 40, "TWN", "Urban", 8, 5, 0, 9, 40, 11, 938, 1270),
    (401, "Keelung Northern Anchorage", 40, "TWN", "Hills", 8, 3, 4, 8, 15, 11, 955, 1255),
    (402, "Hsinchu Science Park", 40, "TWN", "Urban", 8, 4, 0, 9, 20, 11, 920, 1295),
    (403, "Taichung Coastal Redoubt", 40, "TWN", "Urban", 8, 3, 3, 8, 15, 11, 910, 1330),
    (404, "Yilan Eastern Coast", 40, "TWN", "Mountain", 8, 2, 2, 6, 8, 11, 958, 1300),
    (910, "Taoyuan Air Hub", 40, "TWN", "Plains", 8, 4, 0, 9, 12, 11, 930, 1270),
    (911, "Zhongli Industrial Core", 40, "TWN", "Urban", 8, 3, 0, 8, 8, 11, 928, 1282),
    (912, "Miaoli Coastal Hills", 40, "TWN", "Hills", 8, 2, 0, 7, 6, 11, 915, 1310),
    (913, "Changhua Bagua Ridge", 40, "TWN", "Plains", 8, 3, 0, 8, 8, 11, 908, 1345),
    (914, "Nantou Central Highlands", 40, "TWN", "Mountain", 8, 2, 0, 6, 6, 11, 935, 1350),
    (915, "Hualien Taroko Haven", 40, "TWN", "Mountain", 8, 2, 2, 6, 10, 11, 960, 1335),
    (916, "Su'ao Naval Bastion", 40, "TWN", "Hills", 8, 3, 4, 7, 12, 11, 962, 1285),
    (917, "Danshui Estuary Gate", 40, "TWN", "Plains", 8, 3, 1, 8, 8, 11, 935, 1255),

    # State 41: Kaohsiung-Tsoying Southern Port (TWN, Region 8, Mask 11)
    (410, "Kaohsiung Commercial Port", 41, "TWN", "Urban", 8, 5, 5, 9, 25, 11, 912, 1415),
    (411, "Tsoying Main Fleet Base", 41, "TWN", "Urban", 8, 4, 5, 9, 20, 11, 905, 1395),
    (412, "Tainan Cultural Redoubt", 41, "TWN", "Plains", 8, 3, 2, 8, 12, 11, 902, 1370),
    (413, "Pingtung Airbase Corridor", 41, "TWN", "Plains", 8, 3, 0, 7, 10, 11, 932, 1425),
    (918, "Yunlin Mailiao Shore", 41, "TWN", "Plains", 8, 3, 2, 8, 8, 11, 900, 1360),
    (919, "Chiayi Alishan Foothills", 41, "TWN", "Plains", 8, 3, 0, 8, 8, 11, 915, 1375),
    (920, "Gangshan Air Academy", 41, "TWN", "Plains", 8, 3, 0, 8, 8, 11, 908, 1405),
    (921, "Fengshan Military Citadel", 41, "TWN", "Urban", 8, 3, 0, 8, 8, 11, 918, 1420),
    (922, "Chaozhou Pingtung Plain", 41, "TWN", "Plains", 8, 2, 0, 7, 6, 11, 928, 1435),
    (923, "Hengchun Kenting Cape", 41, "TWN", "Hills", 8, 2, 2, 6, 6, 11, 935, 1445),
    (924, "Taitung Pacific Rim", 41, "TWN", "Plains", 8, 2, 2, 6, 8, 11, 948, 1410),
    (925, "Green Island Watchpost", 41, "TWN", "Hills", 8, 1, 1, 5, 4, 11, 965, 1415),

    # State 42: Kinmen (Mask 12) & Penghu (Mask 13) Strait Bastions (TWN, Region 8)
    (420, "Kinmen Island Fortress", 42, "TWN", "Hills", 8, 3, 3, 7, 15, 12, 796, 1338),
    (421, "Penghu Strait Naval Lock", 42, "TWN", "Plains", 8, 3, 4, 7, 15, 13, 856, 1355),
    (926, "Matsu Nangan Fortress", 42, "TWN", "Hills", 8, 2, 2, 6, 10, 12, 820, 1220),
    (927, "Matsu Beigan Bastion", 42, "TWN", "Hills", 8, 2, 2, 6, 8, 12, 824, 1215),

    # State 50: Okinawa & Ryukyu USPACOM Keystone (USA, Region 9, Mask 10)
    (500, "Naha Port Terminal", 50, "USA", "Urban", 9, 4, 4, 8, 20, 10, 1150, 1235),
    (501, "Kadena Air Base Hub", 50, "USA", "Plains", 9, 5, 0, 9, 20, 10, 1162, 1208),
    (502, "White Beach USN Anchorage", 50, "USA", "Hills", 9, 3, 4, 7, 15, 10, 1172, 1220),
    (930, "Futenma Marine Air Station", 50, "USA", "Urban", 9, 3, 0, 8, 10, 10, 1155, 1225),
    (931, "Camp Schwab North Jungle", 50, "USA", "Forest", 9, 2, 1, 7, 8, 10, 1175, 1200),
    (932, "Miyako Air Defense Post", 50, "USA", "Plains", 9, 2, 2, 6, 8, 10, 1070, 1285),
    (933, "Ishigaki Western Bastion", 50, "USA", "Hills", 9, 2, 2, 6, 8, 10, 1055, 1290),

    # State 51: Guam & Mariana Strategic Hub (USA, Region 10, Mask 21)
    (510, "Apra Harbor Fleet Anchor", 51, "USA", "Urban", 10, 5, 5, 9, 20, 21, 1765, 1585),
    (511, "Andersen Strategic Bastion", 51, "USA", "Plains", 10, 4, 0, 8, 20, 21, 1785, 1555),
    (934, "Saipan Strategic Airfield", 51, "USA", "Plains", 10, 2, 2, 7, 10, 21, 1805, 1490),
    (935, "Tinian Atomic Heritage Pier", 51, "USA", "Plains", 10, 2, 2, 6, 8, 21, 1800, 1515),
    (936, "Rota Island Watchpost", 51, "USA", "Hills", 10, 1, 1, 5, 5, 21, 1785, 1545),
    (937, "Orote Peninsula Fleet Lock", 51, "USA", "Hills", 10, 3, 3, 7, 8, 21, 1760, 1595),

    # State 60: Tonkin / Red River Delta (VNM, Region 11, Mask 14)
    (600, "Hanoi Ba Dinh Citadel", 60, "VNM", "Urban", 11, 4, 0, 8, 30, 14, 375, 1465),
    (601, "Haiphong Tonkin Naval Port", 60, "VNM", "Urban", 11, 4, 4, 8, 20, 14, 420, 1485),
    (940, "Lang Son Frontier Gate", 60, "VNM", "Mountain", 11, 3, 0, 7, 10, 14, 395, 1445),
    (941, "Cao Bang Mountain Redoubt", 60, "VNM", "Mountain", 11, 2, 0, 6, 8, 14, 360, 1435),
    (942, "Ha Giang Frontier Watch", 60, "VNM", "Mountain", 11, 2, 0, 5, 6, 14, 325, 1435),
    (943, "Lao Cai Red River Pass", 60, "VNM", "Mountain", 11, 2, 0, 6, 8, 14, 295, 1445),
    (944, "Thai Nguyen Iron Bastion", 60, "VNM", "Hills", 11, 3, 0, 7, 8, 14, 375, 1445),
    (945, "Bac Ninh Delta Artery", 60, "VNM", "Plains", 11, 3, 0, 8, 8, 14, 390, 1465),
    (946, "Quang Ninh Ha Long Port", 60, "VNM", "Hills", 11, 3, 3, 7, 10, 14, 430, 1475),
    (947, "Nam Dinh Southern Hub", 60, "VNM", "Plains", 11, 2, 0, 7, 8, 14, 395, 1495),
    (948, "Ninh Binh Karst Gate", 60, "VNM", "Hills", 11, 2, 0, 7, 6, 14, 375, 1510),
    (949, "Thanh Hoa North Pass", 60, "VNM", "Plains", 11, 3, 0, 7, 8, 14, 355, 1535),
    (950, "Vinh Song Lam Command", 60, "VNM", "Urban", 11, 3, 2, 7, 10, 14, 385, 1575),
    (951, "Ha Tinh Coastal Corridor", 60, "VNM", "Plains", 11, 2, 1, 6, 6, 14, 415, 1595),

    # State 68: Annam / Central Coastal Corridor (VNM, Region 11, Mask 14)
    (602, "Da Nang Central Coastal Base", 68, "VNM", "Hills", 11, 3, 4, 7, 15, 14, 475, 1635),
    (952, "Dong Hoi Coastal Haven", 68, "VNM", "Plains", 11, 2, 2, 6, 6, 14, 435, 1615),
    (953, "Quang Tri DMZ Corridor", 68, "VNM", "Plains", 11, 2, 0, 6, 8, 14, 450, 1630),
    (954, "Hue Ancient Citadel", 68, "VNM", "Urban", 11, 3, 0, 7, 12, 14, 465, 1645),
    (955, "Hoi An Coastal Anchorage", 68, "VNM", "Plains", 11, 2, 2, 6, 6, 14, 485, 1660),
    (956, "Quang Ngai Transit Redoubt", 68, "VNM", "Plains", 11, 2, 0, 6, 6, 14, 495, 1685),
    (957, "Qui Nhon Maritime Terminal", 68, "VNM", "Plains", 11, 3, 3, 7, 10, 14, 515, 1715),
    (958, "Tuy Hoa Coastal Basin", 68, "VNM", "Plains", 11, 2, 1, 6, 6, 14, 520, 1745),
    (959, "Pleiku Central Highlands", 68, "VNM", "Hills", 11, 2, 0, 6, 8, 14, 465, 1705),
    (960, "Kon Tum Highland Redoubt", 68, "VNM", "Mountain", 11, 2, 0, 5, 6, 14, 455, 1685),
    (961, "Buon Ma Thuot Highlands", 68, "VNM", "Hills", 11, 2, 0, 6, 8, 14, 465, 1740),

    # State 69: Cochinchina / Cam Ranh & Mekong Delta (VNM, Region 11, Mask 14)
    (603, "Cam Ranh Bay Deep Anchorage", 69, "VNM", "Plains", 11, 4, 5, 7, 20, 14, 495, 1765),
    (962, "Nha Trang Deep Bay", 69, "VNM", "Urban", 11, 3, 3, 7, 12, 14, 510, 1775),
    (963, "Phan Rang Coastal Haven", 69, "VNM", "Plains", 11, 2, 2, 6, 6, 14, 505, 1805),
    (964, "Phan Thiet Southern Coast", 69, "VNM", "Plains", 11, 2, 2, 6, 6, 14, 475, 1825),
    (965, "Ho Chi Minh City Saigon Core", 69, "VNM", "Urban", 11, 5, 4, 9, 30, 14, 425, 1815),
    (966, "Bien Hoa Armored Depot", 69, "VNM", "Urban", 11, 3, 0, 8, 10, 14, 435, 1800),
    (967, "Vung Tau Maritime Bastion", 69, "VNM", "Plains", 11, 3, 4, 8, 12, 14, 440, 1835),
    (968, "My Tho Mekong Delta Gate", 69, "VNM", "Plains", 11, 2, 2, 7, 8, 14, 415, 1830),
    (969, "Can Tho Delta Capital", 69, "VNM", "Urban", 11, 3, 2, 7, 12, 14, 385, 1845),
    (970, "Rach Gia Gulf Haven", 69, "VNM", "Plains", 11, 2, 2, 6, 6, 14, 360, 1840),
    (971, "Ca Mau Southernmost Cape", 69, "VNM", "Plains", 11, 2, 1, 5, 6, 14, 365, 1865),
    (972, "Long Xuyen Border Crossing", 69, "VNM", "Plains", 11, 2, 0, 6, 6, 14, 380, 1820),
    (973, "Tay Ninh Frontier Redoubt", 69, "VNM", "Forest", 11, 2, 0, 6, 6, 14, 395, 1795),

    # State 61: Luzon & Capital Bay Archipelago (PHL, Region 12, Mask 20)
    (610, "Manila Intramuros Core", 61, "PHL", "Urban", 12, 4, 4, 8, 30, 20, 975, 1680),
    (611, "Subic Bay Fleet Anchorage", 61, "PHL", "Urban", 12, 5, 5, 9, 20, 20, 940, 1665),
    (612, "Clark Air Base Corridor", 61, "PHL", "Plains", 12, 4, 0, 8, 20, 20, 965, 1635),
    (980, "Quezon City Federal District", 61, "PHL", "Urban", 12, 3, 0, 8, 10, 20, 980, 1675),
    (981, "Cavite Fleet Anchorage", 61, "PHL", "Urban", 12, 3, 3, 8, 10, 20, 970, 1690),
    (982, "Baguio Cordillera Command", 61, "PHL", "Mountain", 12, 2, 0, 6, 8, 20, 965, 1595),
    (983, "San Fernando La Union Port", 61, "PHL", "Hills", 12, 2, 3, 6, 6, 20, 955, 1590),
    (984, "Vigan Historical Shore", 61, "PHL", "Plains", 12, 2, 2, 6, 6, 20, 950, 1560),
    (985, "Laoag North Air Hub", 61, "PHL", "Plains", 12, 2, 2, 7, 8, 20, 950, 1535),
    (986, "Aparri Cagayan Delta Gate", 61, "PHL", "Plains", 12, 2, 2, 6, 8, 20, 985, 1535),
    (987, "Tuguegarao Valley Command", 61, "PHL", "Plains", 12, 2, 0, 6, 6, 20, 985, 1565),
    (988, "Cabanatuan Central Depot", 61, "PHL", "Plains", 12, 2, 0, 7, 6, 20, 975, 1640),
    (989, "Tarlac Armored Corridor", 61, "PHL", "Plains", 12, 3, 0, 7, 8, 20, 955, 1640),
    (990, "Dagupan Lingayen Haven", 61, "PHL", "Plains", 12, 2, 2, 7, 8, 20, 950, 1615),
    (991, "Lucena Southern Luzon", 61, "PHL", "Hills", 12, 2, 1, 7, 6, 20, 995, 1710),
    (992, "Naga Bicol Peninsula Core", 61, "PHL", "Plains", 12, 2, 2, 6, 6, 20, 1030, 1725),
    (993, "Legazpi Mayon Fortress", 61, "PHL", "Hills", 12, 2, 2, 6, 8, 20, 1055, 1745),

    # State 70: Visayas & Southern Maritime Straits (PHL, Region 12, Mask 20)
    (613, "Batangas Southern Strait", 70, "PHL", "Hills", 12, 2, 3, 7, 10, 20, 985, 1725),
    (994, "Cebu City Queen Port", 70, "PHL", "Urban", 12, 4, 4, 8, 20, 20, 1030, 1825),
    (995, "Iloilo Panay Trading Post", 70, "PHL", "Urban", 12, 3, 3, 7, 12, 20, 995, 1815),
    (996, "Bacolod Sugar Heartland", 70, "PHL", "Plains", 12, 2, 2, 7, 8, 20, 1015, 1825),
    (997, "Tacloban Leyte Anchorage", 70, "PHL", "Plains", 12, 2, 3, 6, 8, 20, 1085, 1795),
    (998, "Calbayog Samar Coastal Gate", 70, "PHL", "Hills", 12, 2, 2, 6, 6, 20, 1075, 1775),
    (999, "Tagbilaran Bohol Strait", 70, "PHL", "Hills", 12, 2, 2, 6, 6, 20, 1035, 1850),
    (1000, "Dumaguete Negros Haven", 70, "PHL", "Hills", 12, 2, 2, 6, 6, 20, 1005, 1860),
    (1001, "Puerto Princesa Palawan", 70, "PHL", "Forest", 12, 2, 3, 6, 8, 20, 875, 1855),
    (1002, "Davao Gulf Command", 70, "PHL", "Urban", 12, 3, 3, 7, 15, 20, 1090, 1945),
    (1003, "Cagayan de Oro North Shore", 70, "PHL", "Plains", 12, 2, 3, 6, 10, 20, 1065, 1895),
    (1004, "Zamboanga Southwest Gate", 70, "PHL", "Hills", 12, 2, 3, 6, 10, 20, 1025, 1930),
    (1005, "General Santos Southern Bay", 70, "PHL", "Plains", 12, 2, 2, 6, 8, 20, 1075, 1985),
    (1006, "Iligan Industrial Steel Core", 70, "PHL", "Hills", 12, 2, 2, 6, 8, 20, 1050, 1910),
    (1007, "Cotabato Mindanao Basin", 70, "PHL", "Plains", 12, 2, 1, 6, 8, 20, 1050, 1945),

    # State 62: Ulaanbaatar & Trans-Mongolian Steppe (MNG, Region 13, Mask 5)
    (620, "Ulaanbaatar Steppe Command", 62, "MNG", "Urban", 13, 3, 0, 7, 25, 5, 415, 315),
    (621, "Darkhan Northern Junction", 62, "MNG", "Plains", 13, 2, 0, 6, 10, 5, 210, 255),
    (622, "Sainshand Gobi Artery", 62, "MNG", "Plains", 13, 2, 0, 6, 10, 5, 605, 405),
    (1010, "Erdenet Copper Mountain", 62, "MNG", "Mountain", 13, 2, 0, 6, 8, 5, 340, 260),
    (1011, "Sukhbaatar Trans-Baikal", 62, "MNG", "Plains", 13, 2, 0, 6, 8, 5, 265, 225),
    (1012, "Zuunmod Steppe Sector", 62, "MNG", "Plains", 13, 2, 0, 6, 6, 5, 435, 335),
    (1013, "Choibalsan Steppe Command", 62, "MNG", "Plains", 13, 2, 0, 6, 10, 5, 715, 310),
    (1014, "Undurkhaan Khentii Core", 62, "MNG", "Plains", 13, 2, 0, 6, 6, 5, 580, 305),
    (1015, "Baruun-Urt Steppe Depot", 62, "MNG", "Plains", 13, 2, 0, 6, 6, 5, 660, 355),
    (1016, "Dalanzadgad South Gobi", 62, "MNG", "Plains", 13, 2, 0, 5, 6, 5, 375, 435),
    (1017, "Mandalgovi Middle Gobi", 62, "MNG", "Plains", 13, 2, 0, 5, 6, 5, 460, 395),
    (1018, "Arvaikheer Khangai Basin", 62, "MNG", "Plains", 13, 2, 0, 5, 6, 5, 295, 360),
    (1019, "Tsetserleg Mountain Pass", 62, "MNG", "Mountain", 13, 1, 0, 5, 5, 5, 235, 325),
    (1020, "Bayankhongor South Pass", 62, "MNG", "Mountain", 13, 1, 0, 5, 5, 5, 185, 385),
    (1021, "Altai Gobi-Altai Bastion", 62, "MNG", "Mountain", 13, 1, 0, 5, 5, 5, 110, 360),
    (1022, "Khovd Western Command", 62, "MNG", "Mountain", 13, 1, 0, 5, 6, 5, 65, 310),
    (1023, "Ulaangom Great Lakes", 62, "MNG", "Plains", 13, 1, 0, 5, 6, 5, 85, 250),

    # State 63: Central Thailand & Bangkok (THA, Region 14, Mask 17)
    (630, "Bangkok Grand Palace Basin", 63, "THA", "Urban", 14, 4, 3, 8, 30, 17, 230, 1715),
    (631, "Sattahip Royal Naval Port", 63, "THA", "Plains", 14, 3, 4, 7, 15, 17, 265, 1758),
    (1030, "Ayutthaya Chao Phraya", 63, "THA", "Plains", 14, 3, 0, 7, 10, 17, 235, 1685),
    (1031, "Nonthaburi Riverine Hub", 63, "THA", "Urban", 14, 3, 0, 8, 8, 17, 228, 1705),
    (1032, "Samut Prakan Port", 63, "THA", "Urban", 14, 3, 3, 8, 10, 17, 240, 1725),
    (1033, "Chonburi Eastern Seaboard", 63, "THA", "Urban", 14, 3, 3, 8, 12, 17, 260, 1735),
    (1034, "Rayong Petrochemical Port", 63, "THA", "Plains", 14, 3, 3, 8, 10, 17, 285, 1765),
    (1035, "Chanthaburi Maritime Haven", 63, "THA", "Plains", 14, 2, 2, 6, 8, 17, 320, 1765),
    (1036, "Trat Border Gate", 63, "THA", "Plains", 14, 2, 2, 6, 6, 17, 340, 1785),
    (1037, "Nakhon Pathom Plain", 63, "THA", "Plains", 14, 2, 0, 7, 8, 17, 205, 1715),
    (1038, "Kanchanaburi River Pass", 63, "THA", "Mountain", 14, 2, 0, 6, 8, 17, 185, 1695),
    (1039, "Suphan Buri Core", 63, "THA", "Plains", 14, 2, 0, 6, 6, 17, 205, 1675),
    (1040, "Lopburi Armored Center", 63, "THA", "Urban", 14, 3, 0, 7, 10, 17, 245, 1655),
    (1041, "Saraburi North Gateway", 63, "THA", "Hills", 14, 2, 0, 7, 6, 17, 260, 1675),

    # State 71: Khorat Plateau & Southern Peninsula (THA, Region 14, Mask 17)
    (632, "Nakhon Ratchasima Gateway", 71, "THA", "Plains", 14, 2, 0, 6, 12, 17, 285, 1665),
    (633, "Hat Yai Southern Junction", 71, "THA", "Forest", 14, 2, 0, 6, 10, 17, 225, 1895),
    (1042, "Khon Kaen Northeast Core", 71, "THA", "Urban", 14, 3, 0, 7, 12, 17, 315, 1615),
    (1043, "Udon Thani Air Base Hub", 71, "THA", "Plains", 14, 3, 0, 7, 12, 17, 315, 1565),
    (1044, "Ubon Ratchathani Mekong", 71, "THA", "Plains", 14, 3, 0, 7, 10, 17, 380, 1645),
    (1045, "Buriram Khorat Gateway", 71, "THA", "Plains", 14, 2, 0, 6, 6, 17, 325, 1665),
    (1046, "Surin South Plain Redoubt", 71, "THA", "Plains", 14, 2, 0, 6, 6, 17, 350, 1665),
    (1047, "Chiang Mai Lanna Fortress", 71, "THA", "Mountain", 14, 3, 0, 7, 15, 17, 185, 1485),
    (1048, "Chiang Rai Golden Triangle", 71, "THA", "Mountain", 14, 2, 0, 6, 8, 17, 220, 1455),
    (1049, "Phitsanulok Command", 71, "THA", "Plains", 14, 3, 0, 7, 10, 17, 235, 1585),
    (1050, "Surat Thani Gulf Terminal", 71, "THA", "Plains", 14, 2, 3, 6, 8, 17, 215, 1835),
    (1051, "Phuket Andaman Bastion", 71, "THA", "Hills", 14, 3, 4, 7, 12, 17, 195, 1865),
    (1052, "Nakhon Si Thammarat Port", 71, "THA", "Plains", 14, 2, 2, 6, 8, 17, 235, 1860),
    (1053, "Songkhla Gulf Gate", 71, "THA", "Plains", 14, 2, 3, 7, 10, 17, 245, 1895),

    # State 64: Vientiane & Mekong Highlands (LAO, Region 15, Mask 15)
    (640, "Vientiane Mekong Seat", 64, "LAO", "Urban", 15, 2, 0, 6, 20, 15, 325, 1585),
    (641, "Luang Prabang Mountain Pass", 64, "LAO", "Mountain", 15, 1, 0, 5, 10, 15, 285, 1510),
    (1060, "Savannakhet Mekong Crossing", 64, "LAO", "Plains", 15, 2, 0, 6, 10, 15, 360, 1630),
    (1061, "Pakse South Mekong Hub", 64, "LAO", "Plains", 15, 2, 0, 6, 8, 15, 395, 1670),
    (1062, "Champasak Khmer Border", 64, "LAO", "Plains", 15, 1, 0, 5, 5, 15, 405, 1690),
    (1063, "Thakhek Central River Port", 64, "LAO", "Plains", 15, 2, 0, 6, 6, 15, 345, 1600),
    (1064, "Phonsavan Plain of Jars", 64, "LAO", "Mountain", 15, 1, 0, 5, 6, 15, 320, 1545),
    (1065, "Xam Neua North Redoubt", 64, "LAO", "Mountain", 15, 1, 0, 5, 5, 15, 350, 1515),
    (1066, "Oudomxay North Crossroad", 64, "LAO", "Mountain", 15, 1, 0, 5, 5, 15, 265, 1475),
    (1067, "Luang Namtha Border Post", 64, "LAO", "Mountain", 15, 1, 0, 5, 5, 15, 245, 1450),
    (1068, "Bokeo Mekong Haven", 64, "LAO", "Hills", 15, 1, 0, 5, 5, 15, 235, 1470),
    (1069, "Sainyabuli West Ridge", 64, "LAO", "Mountain", 15, 1, 0, 5, 5, 15, 265, 1530),
    (1070, "Salavan Bolaven Plateau", 64, "LAO", "Hills", 15, 1, 0, 5, 5, 15, 415, 1650),
    (1071, "Attapeu Annamite Frontier", 64, "LAO", "Mountain", 15, 1, 0, 5, 5, 15, 435, 1675),

    # State 65: Phnom Penh & Tonle Sap (KHM, Region 16, Mask 16)
    (650, "Phnom Penh Royal Citadel", 65, "KHM", "Urban", 16, 2, 1, 6, 20, 16, 385, 1760),
    (651, "Kompong Som Coastal Haven", 65, "KHM", "Plains", 16, 2, 3, 6, 15, 16, 345, 1798),
    (1072, "Siem Reap Angkor Gateway", 65, "KHM", "Plains", 16, 2, 0, 6, 10, 16, 345, 1720),
    (1073, "Battambang West Granary", 65, "KHM", "Plains", 16, 2, 0, 6, 8, 16, 315, 1720),
    (1074, "Poipet Thai Border Pass", 65, "KHM", "Plains", 16, 2, 0, 6, 6, 16, 295, 1715),
    (1075, "Kompong Cham Mekong Artery", 65, "KHM", "Plains", 16, 2, 0, 6, 8, 16, 395, 1745),
    (1076, "Kratie Mekong River Port", 65, "KHM", "Plains", 16, 2, 0, 6, 6, 16, 415, 1725),
    (1077, "Stung Treng North Junction", 65, "KHM", "Plains", 16, 2, 0, 6, 6, 16, 415, 1695),
    (1078, "Pursat Cardamom Foothills", 65, "KHM", "Hills", 16, 1, 0, 5, 5, 16, 340, 1750),
    (1079, "Koh Kong Coastal Haven", 65, "KHM", "Hills", 16, 1, 2, 5, 6, 16, 320, 1775),
    (1080, "Kampot Mountain Gate", 65, "KHM", "Hills", 16, 2, 2, 6, 6, 16, 365, 1795),
    (1081, "Svay Rieng Parrot's Beak", 65, "KHM", "Plains", 16, 2, 0, 6, 6, 16, 415, 1780),
    (1082, "Prey Veng Eastern Plain", 65, "KHM", "Plains", 16, 1, 0, 5, 5, 16, 405, 1765),
    (1083, "Banlung Ratanakiri Highlands", 65, "KHM", "Mountain", 16, 1, 0, 5, 5, 16, 440, 1705),

    # State 66: Lower Myanmar & Irrawaddy Delta (MMR, Region 17, Mask 18)
    (660, "Yangon Shwedagon Port", 66, "MMR", "Urban", 17, 3, 3, 7, 25, 18, 95, 1615),
    (662, "Mawlamyine Coastal Terminal", 66, "MMR", "Hills", 17, 2, 2, 6, 10, 18, 155, 1665),
    (1090, "Bago Ancient Capital Gate", 66, "MMR", "Urban", 17, 2, 0, 7, 10, 18, 110, 1595),
    (1091, "Pathein Irrawaddy Delta Port", 66, "MMR", "Plains", 17, 2, 2, 6, 8, 18, 65, 1625),
    (1092, "Hinthada Transit Hub", 66, "MMR", "Plains", 17, 2, 0, 6, 6, 18, 75, 1595),
    (1093, "Pyay River Gateway", 66, "MMR", "Plains", 17, 2, 0, 6, 8, 18, 80, 1555),
    (1094, "Taungoo Sittaung Basin", 66, "MMR", "Plains", 17, 2, 0, 6, 6, 18, 125, 1555),
    (1095, "Dawei Tanintharyi Port", 66, "MMR", "Hills", 17, 2, 2, 6, 8, 18, 175, 1735),
    (1096, "Myeik Mergui Haven", 66, "MMR", "Hills", 17, 2, 2, 6, 8, 18, 175, 1780),
    (1097, "Kawthaung South Point", 66, "MMR", "Hills", 17, 1, 2, 5, 6, 18, 170, 1825),
    (1098, "Sittwe Arakan Fleet Port", 66, "MMR", "Urban", 17, 2, 3, 6, 12, 18, 15, 1515),
    (1099, "Kyaukpyu Deep Bay Port", 66, "MMR", "Hills", 17, 2, 3, 6, 10, 18, 30, 1555),
    (1100, "Thandwe Coastal Haven", 66, "MMR", "Hills", 17, 1, 1, 5, 5, 18, 45, 1585),
    (1101, "Thaton Mon Capital", 66, "MMR", "Plains", 17, 2, 1, 6, 6, 18, 135, 1635),

    # State 72: Upper Myanmar & Mandalay (MMR, Region 17, Mask 18)
    (661, "Mandalay Royal Heartland", 72, "MMR", "Plains", 17, 2, 0, 6, 20, 18, 95, 1475),
    (1102, "Naypyidaw Central Redoubt", 72, "MMR", "Plains", 17, 3, 0, 7, 15, 18, 115, 1530),
    (1103, "Meiktila Armored Hub", 72, "MMR", "Plains", 17, 2, 0, 7, 8, 18, 105, 1500),
    (1104, "Magway Oil Basin Core", 72, "MMR", "Plains", 17, 2, 0, 6, 8, 18, 65, 1505),
    (1105, "Chauk Irrawaddy Oilfield", 72, "MMR", "Plains", 17, 2, 0, 6, 8, 18, 60, 1475),
    (1106, "Monywa Chindwin River Port", 72, "MMR", "Plains", 17, 2, 0, 6, 8, 18, 75, 1445),
    (1107, "Sagaing River Bridge Gate", 72, "MMR", "Hills", 17, 2, 0, 6, 6, 18, 90, 1465),
    (1108, "Myingyan River Junction", 72, "MMR", "Plains", 17, 2, 0, 6, 6, 18, 85, 1485),
    (1109, "Taunggyi Shan Command", 72, "MMR", "Mountain", 17, 2, 0, 6, 8, 18, 145, 1500),
    (1110, "Lashio Northern Shan Artery", 72, "MMR", "Mountain", 17, 2, 0, 6, 8, 18, 165, 1435),
    (1111, "Muse China Border Crossing", 72, "MMR", "Mountain", 17, 2, 0, 6, 10, 18, 175, 1405),
    (1112, "Myitkyina Kachin Bastion", 72, "MMR", "Mountain", 17, 2, 0, 6, 10, 18, 120, 1355),
    (1113, "Bhamo Upper Irrawaddy Gate", 72, "MMR", "Hills", 17, 2, 0, 6, 8, 18, 140, 1385),

    # State 67: Malaya Peninsula & Malacca (MYS, Region 17, Mask 19)
    (670, "Kuala Lumpur Federal Seat", 67, "MYS", "Urban", 17, 4, 0, 8, 30, 19, 265, 1965),
    (671, "Port Klang Maritime Lock", 67, "MYS", "Urban", 17, 3, 4, 8, 15, 19, 238, 1975),
    (672, "Johor Bahru Causeway Strait", 67, "MYS", "Urban", 17, 3, 3, 8, 15, 19, 305, 2005),
    (1120, "George Town Penang Fleet Port", 67, "MYS", "Urban", 17, 3, 4, 8, 15, 19, 220, 1950),
    (1121, "Butterworth Mainland Terminal", 67, "MYS", "Urban", 17, 3, 3, 8, 10, 19, 230, 1955),
    (1122, "Ipoh Kinta Valley Titan", 67, "MYS", "Urban", 17, 3, 0, 8, 12, 19, 245, 1960),
    (1123, "Taiping Historic Foothills", 67, "MYS", "Hills", 17, 2, 0, 7, 6, 19, 235, 1955),
    (1124, "Alor Setar Kedah Granary", 67, "MYS", "Plains", 17, 2, 0, 7, 8, 19, 225, 1945),
    (1125, "Kangar Perlis North Pass", 67, "MYS", "Hills", 17, 2, 0, 6, 6, 19, 220, 1935),
    (1126, "Kota Bharu Kelantan Delta", 67, "MYS", "Plains", 17, 2, 2, 7, 8, 19, 265, 1935),
    (1127, "Kuala Terengganu Haven", 67, "MYS", "Plains", 17, 2, 2, 7, 8, 19, 285, 1950),
    (1128, "Kuantan East Coast Port", 67, "MYS", "Plains", 17, 3, 3, 7, 10, 19, 295, 1975),
    (1129, "Seremban Transit Hub", 67, "MYS", "Urban", 17, 2, 0, 8, 8, 19, 275, 1980),
    (1130, "Malacca Historic Fortress", 67, "MYS", "Urban", 17, 3, 3, 8, 15, 19, 280, 1995),
    (1131, "Batu Pahat Coastal Lock", 67, "MYS", "Plains", 17, 2, 2, 7, 8, 19, 295, 2005),
]


def create_theatre_mask(W, H):
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
    return mask


def main():
    root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    states_path = os.path.join(root, "data/map/east_asia_1985_states.json")
    korean_provs_path = os.path.join(root, "data/map/provinces.json")
    out_provs_json = os.path.join(root, "data/map/east_asia_1985_provinces.json")
    out_provs_png = os.path.join(root, "data/map/east_asia_1985_provinces.png")
    out_states_json = states_path

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
        vp = 50 if pid == 0 else (40 if pid == 60 else (25 if pid in (56, 6) else (10 if p.get("supplyHubLevel", 0) >= 3 else 2)))
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
            "victoryPoints": vp,
            "centerX": nx,
            "centerY": ny,
            "adjacencies": list(p["adjacencies"]),
        }

    # 2. Add all 494 Theatre Provinces
    theatre_defs = THEATRE_DEFS_PART1 + THEATRE_DEFS_PART2
    for pdef in theatre_defs:
        pid, name, sid, owner, terrain, sreg, hub, port, infra, vp, rmask, cx, cy = pdef
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
            "victoryPoints": vp,
            "centerX": cx,
            "centerY": cy,
            "adjacencies": [],
        }

    # Helper to add symmetric edge
    def add_edge(u, v, flags="Standard"):
        if u not in all_provinces or v not in all_provinces:
            return
        if u == v:
            return
        u_adj = all_provinces[u]["adjacencies"]
        v_adj = all_provinces[v]["adjacencies"]
        for e in u_adj:
            if e["targetProvinceId"] == v:
                return
        u_adj.append({"targetProvinceId": v, "flags": flags})
        v_adj.append({"targetProvinceId": u, "flags": flags})

    # Group provinces by state for internal Delaunay / MST connectivity
    by_state = defaultdict(list)
    for pid, p in all_provinces.items():
        if pid >= 150: # Theatre provinces
            by_state[p["stateId"]].append(pid)

    for sid, pids in by_state.items():
        n = len(pids)
        if n < 2:
            continue
        # Connect to 2-3 nearest neighbors in same state
        for i in range(n):
            u = pids[i]
            dists = []
            for j in range(n):
                if i != j:
                    v = pids[j]
                    dx = all_provinces[u]["centerX"] - all_provinces[v]["centerX"]
                    dy = all_provinces[u]["centerY"] - all_provinces[v]["centerY"]
                    dists.append((dx * dx + dy * dy, v))
            dists.sort()
            k = min(3, len(dists))
            for _, v in dists[:k]:
                add_edge(u, v, "Standard")

    # Connect adjacent states across borders within same landmask
    theatre_pids = [pid for pid in all_provinces if pid >= 150]
    for i in range(len(theatre_pids)):
        u = theatre_pids[i]
        rm_u = prov_region_mask[u]
        sid_u = all_provinces[u]["stateId"]
        for j in range(i + 1, len(theatre_pids)):
            v = theatre_pids[j]
            rm_v = prov_region_mask[v]
            sid_v = all_provinces[v]["stateId"]
            if rm_u == rm_v and sid_u != sid_v:
                dx = all_provinces[u]["centerX"] - all_provinces[v]["centerX"]
                dy = all_provinces[u]["centerY"] - all_provinces[v]["centerY"]
                dist = math.hypot(dx, dy)
                if dist < 65.0:  # Physically touching border provinces
                    add_edge(u, v, "Standard")

    # Add Cross-Landmask Borders (China <-> Vietnam, China <-> Laos, etc.)
    cross_borders = [
        (254, 940), (254, 943), (252, 600), (250, 601),
        (641, 943), (640, 949), (640, 600),
        (640, 630), (640, 1043), (1060, 1042), (1061, 1044),
        (650, 603), (651, 603), (1081, 965), (1074, 1036), (650, 630),
        (660, 630), (662, 633), (1095, 1050), (1096, 1051),
        (670, 633), (1124, 633), (1125, 633), (1126, 1053),
        (621, 150), (620, 200), (622, 200), (1013, 214), (1011, 161),
    ]
    for u, v in cross_borders:
        add_edge(u, v, "Standard")

    # Add Designated Historical Railways
    railways = [
        # Trans-Siberian / BAM
        (150, 700), (700, 151), (151, 701), (701, 152), (152, 703), (703, 160),
        (160, 709), (709, 161), (161, 162), (162, 163), (163, 173), (173, 170), (170, 171),
        # Jingguang
        (200, 732), (732, 203), (203, 750), (750, 204), (204, 740), (740, 254), (254, 250),
        # Jinghu
        (200, 731), (731, 201), (201, 736), (736, 202), (202, 782), (782, 220),
        (220, 784), (784, 800), (800, 231), (231, 793), (793, 792), (792, 235), (235, 232), (232, 230),
        # Tokaido
        (300, 301), (301, 863), (863, 302), (302, 889), (889, 324), (324, 888),
        (888, 322), (322, 886), (886, 321), (321, 320), (320, 884), (884, 323),
        (323, 892), (892, 331), (331, 330),
    ]
    for u, v in railways:
        add_edge(u, v, "Standard, Railway")

    # Add Designated Rivers
    rivers = [
        (211, 72, "Standard, River"),
        (211, 75, "Standard, River, Railway"),
        (756, 74, "Standard, River"),
        (766, 79, "Standard, River"),
        (768, 116, "Standard, River"),
        (778, 117, "Standard, River"),
        (175, 119, "Standard, River, Railway"),
        (717, 118, "Standard, River"),
        (161, 776, "Standard, River"),
        (162, 772, "Standard, River"),
        (163, 774, "Standard, River"),
    ]
    for u, v, flags in rivers:
        add_edge(u, v, flags)

    # Add Designated Straits
    straits = [
        (56, 330), (56, 905), (905, 330),
        (420, 241), (420, 403), (926, 240), (927, 821),
        (421, 412), (421, 240), (400, 240),
        (311, 305), (181, 707), (182, 314),
        (183, 878), (727, 313), (334, 500),
        (500, 932), (932, 933), (933, 916),
        (986, 923), (611, 410), (610, 500), (613, 510),
        (510, 934), (934, 935), (935, 936), (936, 511),
        (994, 999), (997, 998), (1003, 997), (672, 670),
        (1120, 1121), (848, 855), (849, 854),
    ]
    for u, v in straits:
        add_edge(u, v, "Strait")

    # Update states.json with synchronized province lists
    state_to_provs = defaultdict(list)
    for pid, p in all_provinces.items():
        state_to_provs[p["stateId"]].append(pid)

    for s in states:
        sid = s["stateId"]
        s["provinces"] = sorted(state_to_provs[sid])

    with open(out_states_json, "w", encoding="utf-8") as f:
        json.dump(states, f, indent=2)
    print(f"✅ Synchronized {out_states_json} across {len(states)} states")

    # Verification & Invariant Checks
    assert len(all_provinces) == 618, f"Expected 618 provinces, got {len(all_provinces)}"
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
    print(f"✅ Generated {out_provs_json} (618 geographically projected provinces)")

    # Generate 2048x2048 Lossless PNG Map
    W, H = 2048, 2048
    mask = create_theatre_mask(W, H)

    # Group provinces by regionMask for fast Voronoi
    by_mask = defaultdict(list)
    for pid, p in all_provinces.items():
        rm = prov_region_mask[pid]
        r_val = (pid >> 8) & 0xFF
        g_val = pid & 0xFF
        xw = 0.55 if rm == 6 else 1.0
        by_mask[rm].append((pid, p["centerX"], p["centerY"], r_val, g_val, xw))

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

    # Stamp 3x3 core around every province center for 100% O(1) picking
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

    # Verify mouse picking across all 614 province centers
    for pid, p in all_provinces.items():
        cx, cy = p["centerX"], p["centerY"]
        offset = (cy * W + cx) * 3
        decoded = (rgb[offset] << 8) | rgb[offset + 1]
        assert decoded == pid, f"Mouse pick mismatch at ({cx}, {cy}): expected {pid}, got {decoded}"
    print(f"✅ Verified 100% accurate O(1) mouse picking across all {len(all_provinces)} province centers")

    # Minor OOBs in data/oob_1985/
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
                {"name": "2nd Armored Brigade", "templateId": 35, "startingProvinceId": 621, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": True},
                {"name": "3rd Frontier Division", "templateId": 34, "startingProvinceId": 622, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "THA": {
            "countryTag": "THA",
            "templates": [
                {
                    "templateId": 36,
                    "name": "Royal Thai Infantry Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "IFV", "Artillery", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles", "Artillery"],
                },
                {
                    "templateId": 37,
                    "name": "Royal Thai Cavalry Division",
                    "combatWidth": 16,
                    "battalions": ["MBT", "MBT", "IFV", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "1st Division, King's Guard", "templateId": 36, "startingProvinceId": 630, "startingExperience": 0.55, "startingStrength": 1.0, "pgmAuthorized": True},
                {"name": "2nd Cavalry Division ('Royal Guard')", "templateId": 37, "startingProvinceId": 631, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": True},
                {"name": "3rd Infantry Division", "templateId": 36, "startingProvinceId": 632, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "LAO": {
            "countryTag": "LAO",
            "templates": [
                {
                    "templateId": 38,
                    "name": "Lao People's Armed Forces Brigade",
                    "combatWidth": 16,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "Artillery"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "1st Division ('Vientiane')", "templateId": 38, "startingProvinceId": 640, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "2nd Mountain Brigade", "templateId": 38, "startingProvinceId": 641, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "KHM": {
            "countryTag": "KHM",
            "templates": [
                {
                    "templateId": 39,
                    "name": "KPRAF Infantry Division",
                    "combatWidth": 16,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "196th Infantry Division", "templateId": 39, "startingProvinceId": 650, "startingExperience": 0.35, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "286th Coastal Brigade", "templateId": 39, "startingProvinceId": 651, "startingExperience": 0.35, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "MMR": {
            "countryTag": "MMR",
            "templates": [
                {
                    "templateId": 40,
                    "name": "Myanmar Tatmadaw Light Infantry Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "InfantryKit", "Artillery", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles"],
                },
                {
                    "templateId": 41,
                    "name": "Myanmar Armored Brigade",
                    "combatWidth": 16,
                    "battalions": ["MBT", "IFV", "InfantryKit", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "77th Light Infantry Division", "templateId": 40, "startingProvinceId": 660, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "88th Light Infantry Division", "templateId": 40, "startingProvinceId": 661, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "99th Armored Brigade", "templateId": 41, "startingProvinceId": 662, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
        "MYS": {
            "countryTag": "MYS",
            "templates": [
                {
                    "templateId": 42,
                    "name": "Malaysian Army Royal Malay Regiment",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "IFV", "Artillery", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles", "Artillery"],
                },
                {
                    "templateId": 43,
                    "name": "Malaysian Armored Corps Brigade",
                    "combatWidth": 16,
                    "battalions": ["IFV", "IFV", "SupportVehicles", "Artillery"],
                    "supportCompanies": ["SupportVehicles"],
                },
            ],
            "startingDivisions": [
                {"name": "1st Infantry Division", "templateId": 42, "startingProvinceId": 670, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": False},
                {"name": "2nd Mechanized Brigade", "templateId": 43, "startingProvinceId": 671, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": True},
                {"name": "3rd Infantry Division ('Southern')", "templateId": 42, "startingProvinceId": 672, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False},
            ],
        },
    }

    oob_dir = os.path.join(root, "data/oob_1985")
    for tag, oob in minor_oobs.items():
        p = os.path.join(oob_dir, f"{tag}_oob.json")
        with open(p, "w", encoding="utf-8") as f:
            json.dump(oob, f, indent=2)
        print(f"✅ Generated {p} ({len(oob['startingDivisions'])} divisions)")


if __name__ == "__main__":
    main()
