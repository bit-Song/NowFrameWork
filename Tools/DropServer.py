"""验收辅助：对指定路径只返回前 N 字节后断开，用于模拟传输中断。

用法：python DropServer.py <root> <port> <drop-path> [drop-bytes]
其余路径正常返回，便于先取到完整清单、再在下载 Bundle 时中断。
"""
import os
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.abspath(sys.argv[1])
PORT = int(sys.argv[2])
DROP_PATH = sys.argv[3]
DROP_BYTES = int(sys.argv[4]) if len(sys.argv) > 4 else 100


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def do_GET(self):
        if self.path.split("?")[0] != DROP_PATH:
            return super().do_GET()

        path = os.path.join(ROOT, self.path.lstrip("/").split("?")[0])
        size = os.path.getsize(path)

        self.send_response(200)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(size))
        self.end_headers()

        with open(path, "rb") as f:
            self.wfile.write(f.read(DROP_BYTES))
            self.wfile.flush()

        self.close_connection = True   # 模拟连接中断：剩余字节不再发送
        print("[drop] 中断 " + self.path + " 只发 " + str(DROP_BYTES) + "/" + str(size), flush=True)


print("[drop] root=" + ROOT + " port=" + str(PORT) + " drop=" + DROP_PATH, flush=True)
ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
