"""撮影ページへ飛ぶQRコードを生成する。

本番はサーバーを動かすマシンが変わり、そのたびにIPアドレスも変わる。
Unity の ForgeUI は StreamingAssets/qr.png を読むだけなので、
IPを渡してこのスクリプトを実行し直せばQRを差し替えられる。

使い方:
    python tools/make_qr.py 192.168.1.10        # IPを指定
    python tools/make_qr.py                     # 自動検出させる
    python tools/make_qr.py 192.168.1.10 --port 8080
    python tools/make_qr.py --out qr.png        # 出力先を変える
"""

import argparse
import ipaddress
import socket
import sys
from pathlib import Path

import segno

BASE_DIR = Path(__file__).resolve().parent.parent
DEFAULT_OUT = BASE_DIR / "Hackit_tomodati-sord" / "Assets" / "StreamingAssets" / "qr.png"
DEFAULT_PORT = 8000

# QRはドットが潰れると読めない。スマホのカメラで少し離れても拾える程度に大きくする
SCALE = 12
BORDER = 4


def detect_lan_ip() -> str:
    """このマシンのLAN側IPを推定する。

    外部へUDPソケットを開くふりをして、OSがどのインターフェースを選ぶかを見る。
    実際には送信しないので通信は発生しない。
    """
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.connect(("8.8.8.8", 80))
        return sock.getsockname()[0]
    finally:
        sock.close()


def build_url(host: str, port: int) -> str:
    if not host or "/" in host or ":" in host:
        raise SystemExit(f"[NG] ホストの指定が不正です: {host!r}")

    if not 1 <= port <= 65535:
        raise SystemExit(f"[NG] ポート番号が範囲外です: {port}")

    # 数字とドットだけならIPのつもりのはずなので、厳密に検証する。
    # そうしないと "192.168.1" のような打ち間違いがホスト名として通ってしまう。
    if all(c.isdigit() or c == "." for c in host):
        try:
            ipaddress.ip_address(host)
        except ValueError:
            raise SystemExit(
                f"[NG] IPアドレスとして解釈できません: {host!r}\n"
                f"     ipconfig で確認した Wi-Fi アダプタの IPv4 を指定してください。"
            )

    return f"http://{host}:{port}/"


def main() -> int:
    parser = argparse.ArgumentParser(description="撮影ページへ飛ぶQRコードを生成する")
    parser.add_argument(
        "ip", nargs="?",
        help="サーバーのIPアドレス。省略すると自動検出する",
    )
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help=f"既定 {DEFAULT_PORT}")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="出力先PNG")
    args = parser.parse_args()

    host = args.ip
    if not host:
        host = detect_lan_ip()
        print(f"[INFO] IPを自動検出しました: {host}")
        print("       仮想アダプタ(VirtualBox/VMware)のIPが選ばれることがあります。")
        print("       スマホから繋がらない場合は ipconfig で確認して明示指定してください。")

    url = build_url(host, args.port)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    qr = segno.make(url, error="m")
    qr.save(str(args.out), scale=SCALE, border=BORDER)

    print(f"[OK] QRを保存しました: {args.out}")
    print(f"     読み取り先: {url}")
    print()
    print("Unity側は StreamingAssets/qr.png を読むので、差し替えたら")
    print("ForgeUI の qrImage が空(None)になっていることを確認してください。")
    print("Inspector に画像が刺さっているとそちらが優先されます。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
