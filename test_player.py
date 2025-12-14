# -*- coding: utf-8 -*-
"""
Bilibili 音频播放器测试脚本

本脚本实现以下功能：
1. 通过关键词搜索 B 站视频
2. 获取视频的音频流 URL (DASH 格式)
3. 使用 ffplay 播放音频流

依赖：
- requests: pip install requests
- ffmpeg/ffplay: 需要安装 FFmpeg 并添加到系统 PATH

API 说明：
- 搜索接口: https://api.bilibili.com/x/web-interface/wbi/search/type
- 视频信息接口: https://api.bilibili.com/x/web-interface/view  
- 播放地址接口: https://api.bilibili.com/x/player/wbi/playurl

注意事项：
- B 站 API 需要 WBI 签名鉴权
- 音视频流需要设置正确的 Referer 和 User-Agent 请求头
"""

import subprocess
import time
import urllib.parse
from functools import reduce
from hashlib import md5

import requests

# ==================== 配置区 ====================

KEYWORD = "周杰伦"  # 搜索关键词

# 通用请求头
HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    "Referer": "https://www.bilibili.com/",
    "Accept": "application/json, text/plain, */*",
    "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8",
    "Origin": "https://www.bilibili.com",
}

# ==================== WBI 签名相关 ====================

# WBI 签名混淆表
MIXIN_KEY_ENC_TAB = [
    46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49,
    33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40,
    61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11,
    36, 20, 34, 44, 52
]


def get_mixin_key(orig: str) -> str:
    """对 imgKey 和 subKey 进行字符顺序打乱编码"""
    return reduce(lambda s, i: s + orig[i], MIXIN_KEY_ENC_TAB, "")[:32]


def enc_wbi(params: dict, img_key: str, sub_key: str) -> dict:
    """为请求参数进行 WBI 签名"""
    mixin_key = get_mixin_key(img_key + sub_key)
    curr_time = round(time.time())
    params["wts"] = curr_time
    
    # 按照 key 重排参数
    params = dict(sorted(params.items()))
    
    # 过滤 value 中的 "!'()*" 字符
    params = {
        k: "".join(filter(lambda c: c not in "!'()*", str(v)))
        for k, v in params.items()
    }
    
    # 序列化参数
    query = urllib.parse.urlencode(params)
    
    # 计算 w_rid
    wbi_sign = md5((query + mixin_key).encode()).hexdigest()
    params["w_rid"] = wbi_sign
    
    return params


def get_wbi_keys(session: requests.Session) -> tuple:
    """获取最新的 img_key 和 sub_key"""
    resp = session.get(
        "https://api.bilibili.com/x/web-interface/nav",
        headers=HEADERS
    )
    resp.raise_for_status()
    json_content = resp.json()
    
    img_url = json_content["data"]["wbi_img"]["img_url"]
    sub_url = json_content["data"]["wbi_img"]["sub_url"]
    
    img_key = img_url.rsplit("/", 1)[1].split(".")[0]
    sub_key = sub_url.rsplit("/", 1)[1].split(".")[0]
    
    return img_key, sub_key


# ==================== API 调用函数 ====================

def init_session() -> requests.Session:
    """初始化会话，获取必要的 Cookie"""
    session = requests.Session()
    session.headers.update(HEADERS)
    
    # 先访问 B 站主页，获取 buvid3 等 Cookie
    print("[*] 初始化会话，获取 Cookie...")
    resp = session.get("https://www.bilibili.com/")
    resp.raise_for_status()
    
    print(f"[+] 获取到的 Cookie: {dict(session.cookies)}")
    return session


def search_video(session: requests.Session, keyword: str, img_key: str, sub_key: str) -> dict:
    """
    搜索视频
    
    API: https://api.bilibili.com/x/web-interface/wbi/search/type
    """
    print(f"\n[*] 搜索关键词: {keyword}")
    
    params = {
        "search_type": "video",
        "keyword": keyword,
        "page": 1,
        "page_size": 20,
    }
    
    # WBI 签名
    signed_params = enc_wbi(params, img_key, sub_key)
    
    resp = session.get(
        "https://api.bilibili.com/x/web-interface/wbi/search/type",
        params=signed_params,
        headers=HEADERS
    )
    resp.raise_for_status()
    data = resp.json()
    
    if data["code"] != 0:
        raise Exception(f"搜索失败: {data['message']}")
    
    return data


def get_video_info(session: requests.Session, bvid: str) -> dict:
    """
    获取视频详细信息（包含 cid）
    
    API: https://api.bilibili.com/x/web-interface/view
    """
    print(f"\n[*] 获取视频信息: {bvid}")
    
    resp = session.get(
        "https://api.bilibili.com/x/web-interface/view",
        params={"bvid": bvid},
        headers=HEADERS
    )
    resp.raise_for_status()
    data = resp.json()
    
    if data["code"] != 0:
        raise Exception(f"获取视频信息失败: {data['message']}")
    
    return data


def get_play_url(session: requests.Session, bvid: str, cid: int, img_key: str, sub_key: str) -> dict:
    """
    获取视频播放地址（DASH 格式）
    
    API: https://api.bilibili.com/x/player/wbi/playurl
    """
    print(f"\n[*] 获取播放地址: bvid={bvid}, cid={cid}")
    
    params = {
        "bvid": bvid,
        "cid": cid,
        "fnval": 16,      # DASH 格式
        "fnver": 0,
        "fourk": 1,       # 允许 4K
        "qn": 64,         # 720P (实际上 DASH 格式会返回所有清晰度)
    }
    
    # WBI 签名
    signed_params = enc_wbi(params, img_key, sub_key)
    
    resp = session.get(
        "https://api.bilibili.com/x/player/wbi/playurl",
        params=signed_params,
        headers=HEADERS
    )
    resp.raise_for_status()
    data = resp.json()
    
    if data["code"] != 0:
        raise Exception(f"获取播放地址失败: {data['message']}")
    
    return data


def extract_audio_url(play_url_data: dict) -> tuple:
    """
    从播放地址响应中提取音频流 URL
    
    返回: (audio_url, audio_quality_id)
    
    音质代码:
    - 30216: 64K
    - 30232: 132K  
    - 30280: 192K
    - 30250: 杜比全景声
    - 30251: Hi-Res无损
    """
    dash = play_url_data["data"].get("dash")
    
    if not dash:
        raise Exception("该视频不支持 DASH 格式，无法提取音频流")
    
    audio_list = dash.get("audio")
    
    if not audio_list:
        raise Exception("该视频没有音频轨道")
    
    # 选择最高音质的音频流
    # 按 id 排序（id 越大音质越高）
    audio_list_sorted = sorted(audio_list, key=lambda x: x["id"], reverse=True)
    best_audio = audio_list_sorted[0]
    
    audio_url = best_audio["baseUrl"]
    audio_id = best_audio["id"]
    
    # 音质映射
    quality_map = {
        30216: "64K",
        30232: "132K",
        30280: "192K",
        30250: "杜比全景声",
        30251: "Hi-Res无损",
    }
    quality_name = quality_map.get(audio_id, f"未知({audio_id})")
    
    print(f"[+] 选择音质: {quality_name}")
    print(f"[+] 可用音质: {[quality_map.get(a['id'], a['id']) for a in audio_list]}")
    
    return audio_url, audio_id


def play_audio_with_ffplay(audio_url: str):
    """
    使用 ffplay 播放音频流
    
    关键点: 需要设置 HTTP 请求头（Referer 和 User-Agent），否则会返回 403
    """
    print(f"\n[*] 使用 ffplay 播放音频...")
    print(f"[*] 音频 URL: {audio_url[:100]}...")
    
    # 构建 ffplay 命令
    # 注意: -headers 参数的格式是 "Key: Value\r\n"
    headers_str = (
        f"Referer: https://www.bilibili.com\r\n"
        f"User-Agent: {HEADERS['User-Agent']}\r\n"
    )
    
    cmd = [
        "ffplay",
        "-nodisp",           # 不显示视频窗口
        "-autoexit",         # 播放完毕自动退出
        "-loglevel", "info", # 日志级别
        "-headers", headers_str,
        "-i", audio_url,
    ]
    
    print(f"[*] 执行命令: ffplay -nodisp -autoexit -headers \"Referer: ...\" -i <audio_url>")
    print("\n" + "=" * 50)
    print("开始播放，按 q 键停止...")
    print("=" * 50 + "\n")
    
    try:
        subprocess.run(cmd, check=True)
    except FileNotFoundError:
        print("\n[!] 错误: 未找到 ffplay 命令")
        print("[!] 请确保已安装 FFmpeg 并添加到系统 PATH")
        print("[!] 下载地址: https://ffmpeg.org/download.html")
        print(f"\n[*] 你也可以手动使用以下命令播放:")
        print(f'    ffplay -nodisp -headers "Referer: https://www.bilibili.com" -i "{audio_url}"')
    except subprocess.CalledProcessError as e:
        print(f"\n[!] ffplay 播放出错: {e}")
    except KeyboardInterrupt:
        print("\n[*] 用户中断播放")


def main():
    """主函数"""
    print("=" * 60)
    print("Bilibili 音频播放器测试")
    print("=" * 60)
    
    try:
        # 1. 初始化会话
        session = init_session()
        
        # 2. 获取 WBI 签名密钥
        print("\n[*] 获取 WBI 签名密钥...")
        img_key, sub_key = get_wbi_keys(session)
        print(f"[+] img_key: {img_key}")
        print(f"[+] sub_key: {sub_key}")
        
        # 3. 搜索视频
        search_result = search_video(session, KEYWORD, img_key, sub_key)
        
        # 提取搜索结果
        result_list = search_result["data"]["result"]
        if not result_list:
            print("[!] 搜索结果为空")
            return
        
        # 获取第一个视频
        first_video = result_list[0]
        bvid = first_video["bvid"]
        title = first_video["title"]
        author = first_video["author"]
        duration = first_video["duration"]
        
        # 移除标题中的 HTML 标签
        import re
        title_clean = re.sub(r"<[^>]+>", "", title)
        
        print(f"\n[+] 找到视频:")
        print(f"    标题: {title_clean}")
        print(f"    作者: {author}")
        print(f"    时长: {duration}")
        print(f"    BV号: {bvid}")
        
        # 4. 获取视频详细信息（主要是获取 cid）
        video_info = get_video_info(session, bvid)
        cid = video_info["data"]["cid"]
        print(f"[+] CID: {cid}")
        
        # 5. 获取播放地址
        play_url_data = get_play_url(session, bvid, cid, img_key, sub_key)
        
        # 6. 提取音频流 URL
        audio_url, audio_id = extract_audio_url(play_url_data)
        
        # 7. 使用 ffplay 播放
        play_audio_with_ffplay(audio_url)
        
    except requests.RequestException as e:
        print(f"\n[!] 网络请求错误: {e}")
    except KeyError as e:
        print(f"\n[!] 数据解析错误，缺少字段: {e}")
    except Exception as e:
        print(f"\n[!] 发生错误: {e}")
        import traceback
        traceback.print_exc()


if __name__ == "__main__":
    main()
