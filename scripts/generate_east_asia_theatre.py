#!/usr/bin/env python3
"""
generate_east_asia_theatre.py
Generates the complete 232-province / 36-state East Asian Theatre dataset:
1. data/map/east_asia_1985_provinces.json (preserves 0..119 Korea, adds 150..672 Theatre)
2. data/map/east_asia_1985_provinces.png (1024x1024 RGB8 lossless PNG with O(1) picking)
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

    # Build province -> state map
    prov_to_state = {}
    for s in states:
        for pid in s["provinces"]:
            prov_to_state[pid] = s

    # 1. Collect Korea provinces (0..119)
    all_provinces = {}
    for p in korean_provs:
        pid = p["provinceId"]
        all_provinces[pid] = {
            "provinceId": pid,
            "name": p["name"],
            "stateId": p["stateId"],
            "ownerTag": p["ownerTag"],
            "controllerTag": p["controllerTag"],
            "terrain": p["terrain"],
            "strategicRegionId": p["strategicRegionId"],
            "supplyHubLevel": p["supplyHubLevel"],
            "portLevel": p["portLevel"],
            "infrastructureLevel": p["infrastructureLevel"],
            "centerX": p["centerX"],
            "centerY": p["centerY"],
            "adjacencies": list(p["adjacencies"])
        }

    # 2. Define the 112 Theatre Provinces
    theatre_defs = [
        # State 10: Khabarovsk-Amur BAM Rail Corridor (SOV, Region 1)
        (150, "Irkutsk-Chita Entry Gate", 10, "SOV", "Plains", 1, 5, 0, 8, 220, 40),
        (151, "Taishet-Baikal Junction", 10, "SOV", "Forest", 1, 3, 0, 7, 290, 45),
        (152, "Tynda BAM Central", 10, "SOV", "Forest", 1, 4, 0, 8, 370, 45),
        (153, "Komsomolsk-on-Amur", 10, "SOV", "Urban", 1, 4, 2, 8, 450, 50),
        (160, "Skovorodino Depot", 10, "SOV", "Plains", 1, 3, 0, 7, 530, 50),
        (161, "Belogorsk Rail Hub", 10, "SOV", "Plains", 1, 3, 0, 7, 610, 55),
        (162, "Birobidzhan Terminal", 10, "SOV", "Forest", 1, 3, 0, 7, 690, 60),
        (163, "Khabarovsk Amur Fortress", 10, "SOV", "Urban", 1, 5, 3, 9, 770, 65),

        # State 11: Primorye-Vladivostok Fleet Bastion (SOV, Region 1)
        (170, "Ussuriysk Armored Redoubt", 11, "SOV", "Plains", 1, 4, 0, 8, 780, 110),
        (171, "Vladivostok Golden Horn", 11, "SOV", "Urban", 1, 5, 5, 9, 810, 160),
        (172, "Nakhodka Commercial Port", 11, "SOV", "Hills", 1, 3, 4, 7, 840, 190),
        (173, "Spassk-Dalny Depot", 11, "SOV", "Forest", 1, 2, 0, 6, 790, 85),
        (174, "Arsenyev Aviation Works", 11, "SOV", "Hills", 1, 3, 0, 7, 830, 130),
        (175, "Posyet Bay Frontier", 11, "SOV", "Hills", 1, 3, 3, 7, 760, 150),

        # State 12: Sakhalin & Kuril Island Chain (SOV, Region 2)
        (180, "Yuzhno-Sakhalinsk", 12, "SOV", "Hills", 2, 3, 4, 6, 890, 60),
        (181, "Kholmsk Strait Ferry", 12, "SOV", "Hills", 2, 2, 4, 6, 860, 85),
        (182, "Korsakov Naval Terminal", 12, "SOV", "Plains", 2, 2, 3, 6, 910, 100),
        (183, "Iturup Kuril Bastion", 12, "SOV", "Mountain", 2, 2, 3, 5, 960, 120),

        # State 20: Beijing-Hebei Capital Region (PRC, Region 3)
        (200, "Beijing Central Citadel", 20, "PRC", "Urban", 3, 5, 0, 9, 90, 240),
        (201, "Tianjin Binhai Port", 20, "PRC", "Urban", 3, 4, 4, 8, 130, 260),
        (202, "Tangshan Industrial Basin", 20, "PRC", "Plains", 3, 3, 0, 7, 160, 240),
        (203, "Baoding Southern Gate", 20, "PRC", "Plains", 3, 3, 0, 7, 80, 280),
        (204, "Shijiazhuang Rail Hub", 20, "PRC", "Plains", 3, 4, 0, 8, 70, 320),
        (205, "Qinhuangdao Naval Gate", 20, "PRC", "Hills", 3, 3, 3, 7, 180, 220),

        # State 21: Manchuria-Shenyang Heavy Arsenal (PRC, Region 3)
        (210, "Shenyang Arsenal Hub", 21, "PRC", "Urban", 3, 5, 0, 8, 190, 160),
        (211, "Dandong Yalu Crossing", 21, "PRC", "Plains", 3, 4, 2, 7, 230, 180),
        (212, "Dalian Lushun Fleet Port", 21, "PRC", "Urban", 3, 4, 5, 8, 210, 220),
        (213, "Changchun Rail Center", 21, "PRC", "Plains", 3, 4, 0, 7, 220, 120),
        (214, "Harbin Northern Fortress", 21, "PRC", "Urban", 3, 4, 1, 8, 270, 90),
        (215, "Anshan Steelworks", 21, "PRC", "Plains", 3, 3, 0, 7, 200, 190),

        # State 22: Shandong Peninsula & Qingdao Fleet Base (PRC, Region 4)
        (220, "Jinan Yellow River Gate", 22, "PRC", "Urban", 4, 4, 0, 8, 120, 360),
        (221, "Qingdao North Sea Bastion", 22, "PRC", "Urban", 4, 5, 5, 9, 170, 380),
        (222, "Yantai Bohai Strait Port", 22, "PRC", "Hills", 4, 3, 4, 7, 190, 350),
        (223, "Weihai Fleet Anchorage", 22, "PRC", "Hills", 4, 3, 3, 7, 215, 360),

        # State 23: Shanghai-Yangtze Delta Metropolis (PRC, Region 4)
        (230, "Shanghai Huangpu Core", 23, "PRC", "Urban", 4, 5, 5, 10, 190, 490),
        (231, "Nanjing Yangtze Citadel", 23, "PRC", "Urban", 4, 4, 2, 8, 130, 460),
        (232, "Suzhou Industrial Park", 23, "PRC", "Plains", 4, 3, 0, 8, 170, 480),
        (233, "Hangzhou Qiantang Hub", 23, "PRC", "Urban", 4, 4, 2, 8, 160, 530),
        (234, "Ningbo Zhoushan Anchorage", 23, "PRC", "Hills", 4, 4, 5, 8, 210, 530),
        (235, "Wuxi Grand Canal Basin", 23, "PRC", "Plains", 4, 3, 0, 7, 150, 450),

        # State 24: Fujian Strait Frontline Theater (PRC, Region 5)
        (240, "Fuzhou Strait Command", 24, "PRC", "Urban", 5, 4, 4, 8, 170, 600),
        (241, "Xiamen Frontline Redoubt", 24, "PRC", "Urban", 5, 4, 4, 8, 160, 650),
        (242, "Quanzhou Maritime Hub", 24, "PRC", "Plains", 5, 3, 3, 7, 180, 625),
        (243, "Nanping Mountain Arsenal", 24, "PRC", "Mountain", 5, 2, 0, 6, 120, 590),
        (244, "Longyan Southern Highlands", 24, "PRC", "Mountain", 5, 2, 0, 6, 110, 640),

        # State 25: Guangdong-Shenzhen Special Economic Zone (PRC, Region 5)
        (250, "Guangzhou Pearl Delta Core", 25, "PRC", "Urban", 5, 5, 4, 9, 90, 710),
        (251, "Shenzhen Reform Corridor", 25, "PRC", "Urban", 5, 4, 4, 9, 120, 725),
        (252, "Zhanjiang South Sea Bastion", 25, "PRC", "Plains", 5, 4, 5, 8, 60, 760),
        (253, "Shantou Eastern Port", 25, "PRC", "Plains", 5, 3, 3, 7, 150, 700),
        (254, "Shaoguan Northern Pass", 25, "PRC", "Mountain", 5, 3, 0, 6, 90, 665),

        # State 30: Tokyo-Kanto Economic Megalopolis (JPN, Region 6)
        (300, "Tokyo Chiyoda Citadel", 30, "JPN", "Urban", 6, 5, 4, 10, 920, 420),
        (301, "Yokohama Maritime Hub", 30, "JPN", "Urban", 6, 5, 5, 10, 930, 445),
        (302, "Yokosuka USN Fleet Base", 30, "JPN", "Urban", 6, 4, 5, 9, 940, 465),
        (303, "Chiba Boso Peninsula", 30, "JPN", "Plains", 6, 3, 3, 8, 965, 435),
        (304, "Saitama Northern Transit", 30, "JPN", "Urban", 6, 4, 0, 8, 910, 390),
        (305, "Utsunomiya North Gate", 30, "JPN", "Hills", 6, 3, 0, 7, 925, 360),

        # State 31: Hokkaido Northern Defense Frontier (JPN, Region 6)
        (310, "Sapporo Northern Command", 31, "JPN", "Urban", 6, 4, 2, 8, 910, 230),
        (311, "Hakodate Tsugaru Port", 31, "JPN", "Hills", 6, 3, 4, 7, 890, 275),
        (312, "Asahikawa Armored Center", 31, "JPN", "Plains", 6, 3, 0, 7, 940, 205),
        (313, "Kushiro Pacific Gateway", 31, "JPN", "Plains", 6, 3, 3, 6, 980, 230),
        (314, "Wakkanai Soya Watchpost", 31, "JPN", "Hills", 6, 2, 3, 6, 920, 160),

        # State 32: Kansai-Chubu Industrial Heartland (JPN, Region 7)
        (320, "Osaka Bay Metropolis", 32, "JPN", "Urban", 7, 5, 4, 9, 830, 490),
        (321, "Kyoto Cultural Core", 32, "JPN", "Urban", 7, 3, 0, 8, 840, 465),
        (322, "Nagoya Tokai Arsenal", 32, "JPN", "Urban", 7, 4, 3, 9, 875, 470),
        (323, "Kobe Port Terminal", 32, "JPN", "Urban", 7, 4, 4, 9, 810, 495),
        (324, "Shizuoka Tokaido Route", 32, "JPN", "Hills", 7, 3, 2, 8, 895, 480),

        # State 33: Kyushu-Sasebo Western Maritime Gate (JPN, Region 7)
        (330, "Fukuoka Tsushima Gate", 33, "JPN", "Urban", 7, 4, 4, 9, 750, 560),
        (331, "Kitakyushu Ironworks", 33, "JPN", "Urban", 7, 4, 3, 8, 775, 540),
        (332, "Sasebo Naval Arsenal", 33, "JPN", "Hills", 7, 4, 5, 8, 730, 580),
        (333, "Nagasaki Shipyards", 33, "JPN", "Hills", 7, 3, 4, 8, 740, 610),
        (334, "Kagoshima South Cape", 33, "JPN", "Mountain", 7, 3, 3, 7, 765, 645),

        # State 40: Taipei-Hsinchu Silicon Corridor (TWN, Region 8)
        (400, "Taipei Capital District", 40, "TWN", "Urban", 8, 5, 0, 9, 280, 660),
        (401, "Keelung Northern Anchorage", 40, "TWN", "Hills", 8, 3, 4, 8, 295, 645),
        (402, "Hsinchu Science Park", 40, "TWN", "Urban", 8, 4, 0, 9, 270, 680),
        (403, "Taichung Coastal Redoubt", 40, "TWN", "Urban", 8, 3, 3, 8, 260, 700),
        (404, "Yilan Eastern Coast", 40, "TWN", "Mountain", 8, 2, 2, 6, 295, 680),

        # State 41: Kaohsiung-Tsoying Southern Port & Arsenal (TWN, Region 8)
        (410, "Kaohsiung Commercial Port", 41, "TWN", "Urban", 8, 5, 5, 9, 250, 750),
        (411, "Tsoying Main Fleet Base", 41, "TWN", "Urban", 8, 4, 5, 9, 240, 730),
        (412, "Tainan Cultural Redoubt", 41, "TWN", "Plains", 8, 3, 2, 8, 245, 715),
        (413, "Pingtung Airbase Corridor", 41, "TWN", "Plains", 8, 3, 0, 7, 270, 760),

        # State 42: Kinmen & Penghu Strait Bastions (TWN, Region 8)
        (420, "Kinmen Island Fortress", 42, "TWN", "Hills", 8, 3, 3, 7, 200, 650),
        (421, "Penghu Strait Naval Lock", 42, "TWN", "Plains", 8, 3, 4, 7, 220, 700),

        # State 50: Okinawa & Ryukyu USPACOM Keystone (USA, Region 9)
        (500, "Naha Port Terminal", 50, "USA", "Urban", 9, 4, 4, 8, 480, 680),
        (501, "Kadena Air Base Hub", 50, "USA", "Plains", 9, 5, 0, 9, 490, 660),
        (502, "White Beach USN Anchorage", 50, "USA", "Hills", 9, 3, 4, 7, 505, 670),

        # State 51: Guam & Mariana Strategic Hub (USA, Region 10)
        (510, "Apra Harbor Fleet Anchor", 51, "USA", "Urban", 10, 5, 5, 9, 920, 830),
        (511, "Andersen Strategic Bastion", 51, "USA", "Plains", 10, 4, 0, 8, 935, 810),

        # State 60: Red River Delta & Haiphong-Cam Ranh Corridor (VNM, Region 11)
        (600, "Hanoi Ba Dinh Citadel", 60, "VNM", "Urban", 11, 4, 0, 8, 100, 780),
        (601, "Haiphong Tonkin Naval Port", 60, "VNM", "Urban", 11, 4, 4, 8, 125, 785),
        (602, "Da Nang Central Coastal Base", 60, "VNM", "Hills", 11, 3, 4, 7, 140, 830),
        (603, "Cam Ranh Bay Deep Anchorage", 60, "VNM", "Plains", 11, 4, 5, 7, 160, 880),

        # State 61: Luzon-Manila & Subic Bay Archipelago (PHL, Region 12)
        (610, "Manila Intramuros Core", 61, "PHL", "Urban", 12, 4, 4, 8, 310, 830),
        (611, "Subic Bay Fleet Anchorage", 61, "PHL", "Urban", 12, 5, 5, 9, 290, 815),
        (612, "Clark Air Base Corridor", 61, "PHL", "Plains", 12, 4, 0, 8, 300, 795),
        (613, "Batangas Southern Strait", 61, "PHL", "Hills", 12, 2, 3, 7, 325, 860),

        # State 62: Ulaanbaatar & Trans-Mongolian Steppe (MNG, Region 13)
        (620, "Ulaanbaatar Steppe Command", 62, "MNG", "Urban", 13, 3, 0, 7, 90, 140),
        (621, "Darkhan Northern Junction", 62, "MNG", "Plains", 13, 2, 0, 6, 80, 100),
        (622, "Sainshand Gobi Artery", 62, "MNG", "Plains", 13, 2, 0, 6, 130, 165),

        # State 63: Chao Phraya Basin & Bangkok Metropolis (THA, Region 14)
        (630, "Bangkok Grand Palace Basin", 63, "THA", "Urban", 14, 4, 3, 8, 40, 880),
        (631, "Sattahip Royal Naval Port", 63, "THA", "Plains", 14, 3, 4, 7, 55, 910),
        (632, "Nakhon Ratchasima Gateway", 63, "THA", "Plains", 14, 2, 0, 6, 60, 850),
        (633, "Hat Yai Southern Junction", 63, "THA", "Forest", 14, 2, 0, 6, 45, 950),

        # State 64: Vientiane & Mekong Highlands (LAO, Region 15)
        (640, "Vientiane Mekong Seat", 64, "LAO", "Urban", 15, 2, 0, 6, 45, 795),
        (641, "Luang Prabang Mountain Pass", 64, "LAO", "Mountain", 15, 1, 0, 5, 35, 765),

        # State 65: Phnom Penh & Tonle Sap Basin (KHM, Region 16)
        (650, "Phnom Penh Royal Citadel", 65, "KHM", "Urban", 16, 2, 1, 6, 95, 895),
        (651, "Kompong Som Coastal Haven", 65, "KHM", "Plains", 16, 2, 3, 6, 80, 930),

        # State 66: Irrawaddy Valley & Yangon Coast (MMR, Region 17)
        (660, "Yangon Shwedagon Port", 66, "MMR", "Urban", 17, 3, 3, 7, 25, 720),
        (661, "Mandalay Royal Heartland", 66, "MMR", "Plains", 17, 2, 0, 6, 20, 665),
        (662, "Mawlamyine Coastal Terminal", 66, "MMR", "Hills", 17, 2, 2, 6, 35, 745),

        # State 67: Malaya Peninsula & Strait of Malacca (MYS, Region 17)
        (670, "Kuala Lumpur Federal Seat", 67, "MYS", "Urban", 17, 4, 0, 8, 45, 990),
        (671, "Port Klang Maritime Lock", 67, "MYS", "Urban", 17, 3, 4, 8, 30, 1005),
        (672, "Johor Bahru Causeway Strait", 67, "MYS", "Urban", 17, 3, 3, 8, 65, 1010),
    ]

    for pdef in theatre_defs:
        pid, name, sid, owner, terrain, sreg, hub, port, infra, cx, cy = pdef
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
            "adjacencies": []
        }

    # Helper to add symmetric edge
    def add_edge(u, v, flags):
        if u not in all_provinces or v not in all_provinces:
            raise ValueError(f"Unknown province in edge: {u} <-> {v}")
        if u == v:
            return
        # check if edge already exists
        u_adj = all_provinces[u]["adjacencies"]
        v_adj = all_provinces[v]["adjacencies"]
        for e in u_adj:
            if e["targetProvinceId"] == v:
                return
        u_adj.append({"targetProvinceId": v, "flags": flags})
        v_adj.append({"targetProvinceId": u, "flags": flags})

    # Internal state corridors & theatre cross-border graphs
    # SOV State 10 BAM Line
    bam_line = [150, 151, 152, 153, 160, 161, 162, 163]
    for i in range(len(bam_line) - 1):
        add_edge(bam_line[i], bam_line[i+1], "Standard, Railway")

    # SOV State 11 Primorye
    primorye = [170, 171, 172, 173, 174, 175]
    add_edge(163, 170, "Standard, Railway")
    add_edge(170, 171, "Standard, Railway")
    add_edge(171, 172, "Standard")
    add_edge(170, 173, "Standard")
    add_edge(170, 174, "Standard")
    add_edge(171, 175, "Standard, Railway")
    add_edge(172, 174, "Standard")
    add_edge(173, 163, "Standard")

    # SOV Primorye -> PRK border (Connects Trans-Siberian to Korean Peninsula!)
    add_edge(175, 119, "Standard, Railway")
    add_edge(170, 119, "Standard")

    # SOV State 12 Sakhalin & Kurils
    add_edge(180, 181, "Standard")
    add_edge(180, 182, "Standard, Railway")
    add_edge(182, 183, "Strait")
    add_edge(181, 163, "Strait") # Sakhalin to BAM mainland
    add_edge(183, 310, "Strait") # Kurils to Hokkaido

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
    add_edge(210, 200, "Standard, Railway") # Beijing <-> Shenyang
    add_edge(205, 212, "Standard")
    add_edge(214, 153, "Standard") # Harbin <-> Amur
    add_edge(213, 170, "Standard, Railway") # Changchun <-> Ussuriysk

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
    add_edge(222, 212, "Strait") # Dalian <-> Yantai Bohai Strait

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
    add_edge(311, 305, "Strait, Railway") # Tsugaru Strait Seikan Tunnel
    add_edge(305, 304, "Standard, Railway")
    add_edge(304, 300, "Standard, Railway")
    add_edge(300, 301, "Standard, Railway")
    add_edge(301, 302, "Standard")
    add_edge(302, 300, "Standard, Railway")
    add_edge(300, 303, "Standard")
    add_edge(303, 301, "Standard")

    # JPN State 32 Kansai
    add_edge(300, 324, "Standard, Railway") # Tokaido Shinkansen
    add_edge(324, 322, "Standard, Railway")
    add_edge(322, 321, "Standard, Railway")
    add_edge(321, 320, "Standard, Railway")
    add_edge(320, 323, "Standard, Railway")

    # JPN State 33 Kyushu
    add_edge(323, 331, "Standard, Railway") # Kanmon Tunnel
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
    add_edge(420, 241, "Strait") # Kinmen <-> Xiamen
    add_edge(420, 403, "Strait") # Kinmen <-> Taichung
    add_edge(421, 412, "Strait") # Penghu <-> Tainan
    add_edge(421, 240, "Strait") # Penghu <-> Fuzhou
    add_edge(400, 240, "Strait") # Taipei <-> Fuzhou Strait corridor

    # USA State 50 Okinawa
    add_edge(500, 501, "Standard")
    add_edge(500, 502, "Standard")
    add_edge(501, 502, "Standard")
    add_edge(500, 334, "Strait") # Okinawa <-> Kagoshima (Kyushu)
    add_edge(500, 401, "Strait") # Okinawa <-> Keelung (Taiwan)

    # USA State 51 Guam
    add_edge(510, 511, "Standard")
    add_edge(511, 500, "Strait")
    add_edge(510, 500, "Strait") # Guam <-> Okinawa Sea corridor

    # MNG State 62 Mongolia
    add_edge(621, 620, "Standard, Railway")
    add_edge(620, 622, "Standard, Railway")
    add_edge(621, 150, "Standard, Railway") # Trans-Mongolian to BAM Irkutsk
    add_edge(622, 200, "Standard, Railway") # Trans-Mongolian to Beijing

    # VNM State 60 Vietnam
    add_edge(600, 601, "Standard, Railway")
    add_edge(600, 602, "Standard, Railway")
    add_edge(602, 603, "Standard, Railway")
    add_edge(600, 252, "Standard, Railway") # Hanoi <-> Zhanjiang (PRC)
    add_edge(601, 250, "Standard")

    # LAO State 64 Laos
    add_edge(641, 640, "Standard")
    add_edge(640, 600, "Standard") # Vientiane <-> Hanoi
    add_edge(641, 600, "Standard")

    # KHM State 65 Cambodia
    add_edge(650, 651, "Standard")
    add_edge(650, 603, "Standard") # Phnom Penh <-> Cam Ranh/Mekong
    add_edge(651, 603, "Standard")

    # THA State 63 Thailand
    add_edge(632, 630, "Standard, Railway")
    add_edge(630, 631, "Standard, Railway")
    add_edge(630, 633, "Standard, Railway")
    add_edge(632, 640, "Standard") # Nakhon Ratchasima <-> Vientiane
    add_edge(630, 650, "Standard") # Bangkok <-> Phnom Penh
    add_edge(631, 651, "Standard")

    # MMR State 66 Myanmar
    add_edge(661, 660, "Standard, Railway")
    add_edge(661, 662, "Standard")
    add_edge(660, 662, "Standard")
    add_edge(660, 630, "Standard") # Yangon <-> Bangkok
    add_edge(662, 633, "Standard")

    # MYS State 67 Malaysia
    add_edge(670, 671, "Standard, Railway")
    add_edge(670, 672, "Standard, Railway")
    add_edge(670, 633, "Standard, Railway") # Malaya <-> Hat Yai (Thailand)
    add_edge(671, 672, "Standard")

    # PHL State 61 Philippines
    add_edge(612, 611, "Standard, Railway")
    add_edge(612, 610, "Standard, Railway")
    add_edge(611, 610, "Standard, Railway")
    add_edge(610, 613, "Standard, Railway")
    add_edge(611, 410, "Strait") # Subic Bay <-> Kaohsiung (Luzon Strait)
    add_edge(610, 500, "Strait") # Manila <-> Okinawa Sea corridor
    add_edge(613, 510, "Strait") # Philippines <-> Guam Western Pacific corridor

    # 3. Verification & Validation of Graph Invariants
    assert len(all_provinces) == 232, f"Expected 232 provinces, got {len(all_provinces)}"
    all_pids = sorted(all_provinces.keys())
    state_pids = sorted(prov_to_state.keys())
    assert all_pids == state_pids, "Mismatch between state provinces and defined provinces!"

    # Symmetrical edge check & degree >= 2 check
    for pid, p in all_provinces.items():
        assert len(p["adjacencies"]) >= 2, f"Province {pid} ({p['name']}) has degree < 2 ({len(p['adjacencies'])})"
        for edge in p["adjacencies"]:
            target = edge["targetProvinceId"]
            flags = edge["flags"]
            assert target != pid, f"Self loop on {pid}"
            assert target in all_provinces, f"Unknown target {target} from {pid}"
            target_adj = all_provinces[target]["adjacencies"]
            found = False
            for r_edge in target_adj:
                if r_edge["targetProvinceId"] == pid:
                    assert r_edge["flags"] == flags, f"Mismatched flags {pid}<->{target}: {flags} vs {r_edge['flags']}"
                    found = True
                    break
            assert found, f"Asymmetric adjacency: {pid} -> {target} [{flags}] lacks reverse edge"

    # Save data/map/east_asia_1985_provinces.json
    out_list = [all_provinces[i] for i in sorted(all_provinces.keys())]
    with open(out_provs_json, "w", encoding="utf-8") as f:
        json.dump(out_list, f, indent=2)
    print(f"✅ Generated {out_provs_json} (232 provinces, 100% bijective edges)")

    # 4. Generate data/map/east_asia_1985_provinces.png (1024x1024)
    width, height = 1024, 1024
    # Fill with Ocean Sentinel: 0xFFFF -> R=255, G=255, B=0
    rgb = bytearray([255, 255, 0] * (width * height))

    # To ensure Voronoi cells form realistic contiguous landmasses while keeping ocean intact:
    # We assign each pixel (x, y) to its nearest province center if within max_land_radius.
    # We also explicitly stamp the exact province center.
    coords = []
    for pid, p in all_provinces.items():
        coords.append((pid, p["centerX"], p["centerY"]))

    # Fast spatial grid or nearest neighbor
    max_radius = 26
    max_radius_sq = max_radius * max_radius

    for pid, cx, cy in coords:
        r_val = (pid >> 8) & 0xFF
        g_val = pid & 0xFF
        x_min = max(0, cx - max_radius)
        x_max = min(width - 1, cx + max_radius)
        y_min = max(0, cy - max_radius)
        y_max = min(height - 1, cy + max_radius)

        for y in range(y_min, y_max + 1):
            dy = y - cy
            dy_sq = dy * dy
            row_offset = y * width * 3
            for x in range(x_min, x_max + 1):
                dx = x - cx
                dist_sq = dx * dx + dy_sq
                if dist_sq <= max_radius_sq:
                    pixel_idx = row_offset + x * 3
                    # Overwrite if ocean sentinel or closer
                    if rgb[pixel_idx] == 255 and rgb[pixel_idx + 1] == 255:
                        rgb[pixel_idx] = r_val
                        rgb[pixel_idx + 1] = g_val
                        rgb[pixel_idx + 2] = 0

    # Guarantee center pixels exactly match
    for pid, cx, cy in coords:
        r_val = (pid >> 8) & 0xFF
        g_val = pid & 0xFF
        # 3x3 core around center
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                px = min(max(0, cx + dx), width - 1)
                py = min(max(0, cy + dy), height - 1)
                offset = (py * width + px) * 3
                rgb[offset] = r_val
                rgb[offset + 1] = g_val
                rgb[offset + 2] = 0

    write_png(out_provs_png, width, height, bytes(rgb))
    print(f"✅ Generated {out_provs_png} (1024x1024 RGB8 lossless PNG)")

    # Verify mouse-picking property at all 232 province centers
    for pid, cx, cy in coords:
        offset = (cy * width + cx) * 3
        read_r = rgb[offset]
        read_g = rgb[offset + 1]
        decoded = (read_r << 8) | read_g
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
                    "supportCompanies": ["SupportVehicles", "Artillery"]
                },
                {
                    "templateId": 31,
                    "name": "PAVN Armored Brigade",
                    "combatWidth": 16,
                    "battalions": ["MBT", "MBT", "IFV", "Artillery"],
                    "supportCompanies": ["SupportVehicles"]
                }
            ],
            "startingDivisions": [
                { "name": "308th Division ('Quan Tien')", "templateId": 30, "startingProvinceId": 600, "startingExperience": 0.55, "startingStrength": 1.0, "pgmAuthorized": True },
                { "name": "312th Division ('Song Lam')", "templateId": 30, "startingProvinceId": 601, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "203rd Tank Brigade", "templateId": 31, "startingProvinceId": 602, "startingExperience": 0.60, "startingStrength": 1.0, "pgmAuthorized": True },
                { "name": "9th Division ('Dong Nai')", "templateId": 30, "startingProvinceId": 603, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False }
            ]
        },
        "PHL": {
            "countryTag": "PHL",
            "templates": [
                {
                    "templateId": 32,
                    "name": "Philippine Army Infantry Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "InfantryKit", "SupportVehicles", "Artillery"],
                    "supportCompanies": ["SupportVehicles"]
                },
                {
                    "templateId": 33,
                    "name": "Philippine Light Armored Division",
                    "combatWidth": 16,
                    "battalions": ["IFV", "IFV", "SupportVehicles", "InfantryKit"],
                    "supportCompanies": ["SupportVehicles"]
                }
            ],
            "startingDivisions": [
                { "name": "1st Infantry Division ('Tabak')", "templateId": 32, "startingProvinceId": 610, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "2nd Infantry Division ('Jungle Fighter')", "templateId": 32, "startingProvinceId": 611, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "Light Armor Brigade", "templateId": 33, "startingProvinceId": 612, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": True }
            ]
        },
        "MNG": {
            "countryTag": "MNG",
            "templates": [
                {
                    "templateId": 34,
                    "name": "Mongolian Motor Rifle Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "IFV", "MBT", "Artillery", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles", "Artillery"]
                },
                {
                    "templateId": 35,
                    "name": "Mongolian Cavalry-Armored Brigade",
                    "combatWidth": 16,
                    "battalions": ["MBT", "IFV", "SupportVehicles", "InfantryKit"],
                    "supportCompanies": ["SupportVehicles"]
                }
            ],
            "startingDivisions": [
                { "name": "1st Motor Rifle Division ('Sukhbaatar')", "templateId": 34, "startingProvinceId": 620, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "2nd Armored Brigade", "templateId": 35, "startingProvinceId": 621, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "3rd Frontier Division", "templateId": 34, "startingProvinceId": 622, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False }
            ]
        },
        "THA": {
            "countryTag": "THA",
            "templates": [
                {
                    "templateId": 36,
                    "name": "RTA Infantry Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "SupportVehicles", "Artillery", "IFV"],
                    "supportCompanies": ["SupportVehicles", "Artillery"]
                },
                {
                    "templateId": 37,
                    "name": "RTA Cavalry Division",
                    "combatWidth": 18,
                    "battalions": ["MBT", "IFV", "SupportVehicles", "InfantryKit", "Artillery"],
                    "supportCompanies": ["SupportVehicles"]
                }
            ],
            "startingDivisions": [
                { "name": "1st Division ('King's Guard')", "templateId": 36, "startingProvinceId": 630, "startingExperience": 0.55, "startingStrength": 1.0, "pgmAuthorized": True },
                { "name": "2nd Cavalry Division", "templateId": 37, "startingProvinceId": 631, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "6th Infantry Division", "templateId": 36, "startingProvinceId": 632, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False }
            ]
        },
        "LAO": {
            "countryTag": "LAO",
            "templates": [
                {
                    "templateId": 38,
                    "name": "LPAF Infantry Division",
                    "combatWidth": 18,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "Artillery", "SupportVehicles", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles"]
                }
            ],
            "startingDivisions": [
                { "name": "1st Vientiane Division", "templateId": 38, "startingProvinceId": 640, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "2nd Mountain Brigade", "templateId": 38, "startingProvinceId": 641, "startingExperience": 0.35, "startingStrength": 1.0, "pgmAuthorized": False }
            ]
        },
        "KHM": {
            "countryTag": "KHM",
            "templates": [
                {
                    "templateId": 39,
                    "name": "KPRAF Infantry Division",
                    "combatWidth": 18,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "Artillery", "SupportVehicles", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles"]
                }
            ],
            "startingDivisions": [
                { "name": "179th Infantry Division", "templateId": 39, "startingProvinceId": 650, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "286th Coastal Brigade", "templateId": 39, "startingProvinceId": 651, "startingExperience": 0.35, "startingStrength": 1.0, "pgmAuthorized": False }
            ]
        },
        "MMR": {
            "countryTag": "MMR",
            "templates": [
                {
                    "templateId": 40,
                    "name": "Tatmadaw Light Infantry Division",
                    "combatWidth": 18,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "InfantryKit", "SupportVehicles", "Artillery"],
                    "supportCompanies": ["SupportVehicles"]
                },
                {
                    "templateId": 41,
                    "name": "Tatmadaw Armored Regiment",
                    "combatWidth": 16,
                    "battalions": ["MBT", "IFV", "SupportVehicles", "Artillery"],
                    "supportCompanies": ["SupportVehicles"]
                }
            ],
            "startingDivisions": [
                { "name": "77th Light Infantry Division", "templateId": 40, "startingProvinceId": 660, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "88th Light Infantry Division", "templateId": 40, "startingProvinceId": 661, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "1st Armored Regiment", "templateId": 41, "startingProvinceId": 662, "startingExperience": 0.40, "startingStrength": 1.0, "pgmAuthorized": False }
            ]
        },
        "MYS": {
            "countryTag": "MYS",
            "templates": [
                {
                    "templateId": 42,
                    "name": "Royal Malay Infantry Division",
                    "combatWidth": 20,
                    "battalions": ["InfantryKit", "InfantryKit", "InfantryKit", "IFV", "Artillery", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles", "Artillery"]
                },
                {
                    "templateId": 43,
                    "name": "Malaysian Armored Corps Brigade",
                    "combatWidth": 16,
                    "battalions": ["IFV", "IFV", "MBT", "SupportVehicles"],
                    "supportCompanies": ["SupportVehicles"]
                }
            ],
            "startingDivisions": [
                { "name": "1st Royal Malay Division", "templateId": 42, "startingProvinceId": 670, "startingExperience": 0.50, "startingStrength": 1.0, "pgmAuthorized": False },
                { "name": "2nd Armored Brigade", "templateId": 43, "startingProvinceId": 671, "startingExperience": 0.55, "startingStrength": 1.0, "pgmAuthorized": True },
                { "name": "3rd Infantry Brigade", "templateId": 42, "startingProvinceId": 672, "startingExperience": 0.45, "startingStrength": 1.0, "pgmAuthorized": False }
            ]
        }
    }

    for tag, oob_data in minor_oobs.items():
        oob_file = os.path.join(oob_dir, f"{tag}_oob.json")
        with open(oob_file, "w", encoding="utf-8") as f:
            json.dump(oob_data, f, indent=2)
        print(f"✅ Generated {oob_file} ({len(oob_data['startingDivisions'])} divisions)")

if __name__ == "__main__":
    main()
