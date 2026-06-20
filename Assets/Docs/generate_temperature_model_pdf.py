from pathlib import Path
from PIL import Image, ImageDraw, ImageFont


OUT_DIR = Path("Assets/Docs")
OUT_DIR.mkdir(parents=True, exist_ok=True)
OUT_PATH = OUT_DIR / "WaterTemperatureInfluenceModel_fixed.pdf"
PREVIEW_PATH = OUT_DIR / "WaterTemperatureInfluenceModel_fixed_preview.png"
FONT_PATH = r"C:\Windows\Fonts\NotoSansJP-VF.ttf"

PAGE_WIDTH = 1240
PAGE_HEIGHT = 1754
MARGIN = 90

font_title = ImageFont.truetype(FONT_PATH, 42)
font_h1 = ImageFont.truetype(FONT_PATH, 30)
font_h2 = ImageFont.truetype(FONT_PATH, 25)
font_body = ImageFont.truetype(FONT_PATH, 22)
font_code = ImageFont.truetype(FONT_PATH, 20)
font_small = ImageFont.truetype(FONT_PATH, 18)

content = [
    ("title", "仮想ソナーシステムにおける水温影響モデル"),
    ("h1", "1. 概要"),
    ("p", "本システムでは、水温の鉛直分布に起因する音速変化を考慮し、仮想ソナーの計測距離および音線屈折に反映する。水中音速は深度方向の水温プロファイルから算出し、音速が深度に応じて変化する成層媒質として水中を近似する。"),
    ("h1", "2. 座標系と水深の定義"),
    ("p", "Unity空間では鉛直上向きをY軸正方向とし、水深 d は下向きを正として定義する。ワールド座標における位置のY座標を y とすると、水深は d = -y で与える。したがって、y = 0 を水面、y < 0 を水中とみなす。例えばUnity座標で y = -10 の位置は水深 d = 10 m に対応する。"),
    ("h1", "3. 水温プロファイル"),
    ("p", "水温プロファイルはCSVファイルとして与える。CSVは水深と水温の組で構成される。depth_m は正の水深[m]、temperature_c はその深度における水温[℃]である。"),
    ("code", "depth_m,temperature_c\n0,20\n5,18\n10,15\n20,10"),
    ("p", "任意の水深における水温 T(d) は、CSVに与えられた隣接する水深-水温データ間を線形補間することで求める。テーブル範囲外では、最浅または最深の値をそのまま用いる。"),
    ("p", "d_i と d_(i+1) の間に対象水深 d がある場合、T(d) = T_i + ((d - d_i) / (d_(i+1) - d_i)) × (T_(i+1) - T_i) とする。"),
    ("h1", "4. 音速モデル"),
    ("p", "本システムでは、淡水環境と海水環境の両方に対応するため、音速計算式を選択可能としている。"),
    ("h2", "4.1 淡水モデル"),
    ("p", "淡水、湛水、水槽、貯水池などを対象とする場合は FreshWater モデルを用いる。淡水中の音速 c は水温 T と水深 d の関数として近似する。"),
    ("code", "c(T,d) = c0(T) + 0.0163 d\n\nc0(T) = 1402.388 + 5.03830T - 5.81090e-2 T^2\n        + 3.3432e-4 T^3 - 1.47797e-6 T^4\n        + 3.1419e-9 T^5"),
    ("p", "ここで T は水温[℃]、d は水深[m]、c は音速[m/s]である。0.0163 d は深度による圧力効果を簡易的に表す項である。"),
    ("h2", "4.2 海水モデル"),
    ("p", "海水を対象とする場合は Mackenzie1981 モデルを用いる。この場合、音速は水温 T、塩分 S、水深 d に依存する。"),
    ("code", "c(T,S,d) = 1448.96 + 4.591T - 5.304e-2 T^2\n         + 2.374e-4 T^3 + 1.340(S-35)\n         + 1.630e-2 d + 1.675e-7 d^2\n         - 1.025e-2 T(S-35) - 7.139e-13 T d^3"),
    ("p", "S は塩分[PSU]である。淡水では通常 S = 0、標準的な海水では S はおよそ35とする。"),
    ("h1", "5. 温度影響モード"),
    ("p", "水温影響を比較実験しやすいように、以下の3種類のモードを用意している。"),
    ("p", "None: 水温プロファイルを使用しない。音線は直線として扱い、計測距離も幾何学的距離を用いる。温度影響を含まない基準データの取得に用いる。"),
    ("p", "RangeOnly: 音線は直線のままとし、水温による音速変化を伝搬時間および計測距離にのみ反映する。"),
    ("p", "RefractionAndRange: 水温による音速変化を、音線屈折と計測距離の両方に反映する。"),
    ("h1", "6. 伝搬時間と音響距離"),
    ("p", "各ビームは一定のステップ長 Δs ごとに分割される。各ステップにおける局所音速を c_i、ステップ長を Δs_i とすると、片道伝搬時間 t は t = Σ(Δs_i / c_i) で近似される。"),
    ("p", "温度影響を考慮しない場合、計測距離は幾何学的距離 R_geo = ΣΔs_i である。温度影響を距離に反映する場合、基準音速 c_ref を仮定して、音響距離 R_acoustic = c_ref × t と定義する。"),
    ("p", "出力CSVで追加列を有効にした場合、range_m、geometric_range_m、acoustic_range_m、travel_time_s を保存する。"),
    ("h1", "7. 音線屈折モデル"),
    ("p", "水温分布による音速変化は、鉛直方向にのみ変化する成層媒質として扱う。すなわち音速場は c = c(d) であり、水平方向には一様であると仮定する。"),
    ("p", "この仮定のもとで、音線屈折はSnellの法則に基づく区分層近似により計算する。音線方向を水平成分と鉛直成分に分解し、各ステップで現在深度の音速に応じて進行方向を更新する。"),
    ("p", "本実装では、水平面に対する音線の仰角を θ とし、cos(θ) / c(d) = constant という不変量を保存する。あるステップでの音速を c1、次ステップでの音速を c2、現在の水平成分を cos(θ1) とすると、次の水平成分は cos(θ2) = (c2 / c1) cos(θ1) として求める。"),
    ("p", "cos(θ2) >= 1 となる場合は、音線が転回点に達したものとして扱い、水平に近い方向へ制限した上で鉛直方向の符号を反転させる。"),
    ("h1", "8. 仮定と制限"),
    ("p", "本モデルは厳密な波動音響シミュレーションではなく、仮想ソナー環境における水温影響の傾向評価を目的としたレイトレーシング近似である。"),
    ("p", "主な仮定は、音速が鉛直方向のみに変化すること、水平方向の温度・音速変化を考慮しないこと、流れ・乱流・散乱・吸収・周波数依存性を考慮しないこと、ソナーの送受波器特性やサイドローブを簡略化していることである。"),
    ("p", "したがって、本モデルは実機ソナーの完全再現ではなく、水温成層による計測点群の変位傾向を確認するための実験用モデルである。"),
    ("h1", "9. 比較実験の方法"),
    ("p", "水温影響を評価する場合、同一シーン・同一ソナー姿勢・同一対象物に対して、None、RangeOnly、RefractionAndRange の3モードで点群を取得し比較する。これにより、水温プロファイルが計測距離に与える影響と、音線屈折による空間的な点群変位を分離して評価できる。"),
]


def wrap_text(draw, text, font, max_width):
    result = []
    for paragraph in text.split("\n"):
        line = ""
        for char in paragraph:
            test = line + char
            bbox = draw.textbbox((0, 0), test, font=font)
            if bbox[2] - bbox[0] <= max_width:
                line = test
            else:
                if line:
                    result.append(line)
                line = char
        result.append(line)
    return result


def make_page():
    image = Image.new("RGB", (PAGE_WIDTH, PAGE_HEIGHT), "white")
    return image, ImageDraw.Draw(image)


pages = []
page, draw = make_page()
y = MARGIN
page_no = 1


def finish_page():
    global page, draw, y, page_no
    draw.text((PAGE_WIDTH - MARGIN - 80, PAGE_HEIGHT - 55), f"{page_no}", fill=(120, 120, 120), font=font_small)
    pages.append(page)
    page_no += 1
    page, draw = make_page()
    y = MARGIN


def add_block(kind, text):
    global y
    if kind == "title":
        font, gap, before = font_title, 28, 0
    elif kind == "h1":
        font, gap, before = font_h1, 18, 25
    elif kind == "h2":
        font, gap, before = font_h2, 14, 18
    elif kind == "code":
        font, gap, before = font_code, 10, 10
    else:
        font, gap, before = font_body, 13, 8

    y += before
    lines = wrap_text(draw, text, font, PAGE_WIDTH - 2 * MARGIN)
    line_height = font.size + (10 if kind != "code" else 8)
    needed_height = len(lines) * line_height + gap
    if y + needed_height > PAGE_HEIGHT - MARGIN:
        finish_page()

    if kind == "code":
        box_height = len(lines) * line_height + 24
        draw.rectangle((MARGIN - 15, y - 8, PAGE_WIDTH - MARGIN + 15, y + box_height), fill=(245, 245, 245), outline=(220, 220, 220))
        y += 8

    for line in lines:
        draw.text((MARGIN, y), line, fill=(25, 25, 25), font=font)
        y += line_height
    y += gap


for block_kind, block_text in content:
    add_block(block_kind, block_text)

finish_page()
pages[0].save(OUT_PATH, save_all=True, append_images=pages[1:], resolution=150.0)
pages[0].save(PREVIEW_PATH)

print(OUT_PATH.resolve())
print(PREVIEW_PATH.resolve())
