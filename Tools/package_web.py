"""Prepare a Unity WebGL build for static hosting.

Run after the Unity build and before `vercel deploy`:

    python Tools/package_web.py [path/to/Build/WebGL]

Does two things the Unity build does not:

1. Moves the four build artifacts into a folder named after their content hash.
   Unity reuses the same filenames on every build, so a long-lived cache entry
   pins a returning visitor to a stale wasm/data while index.html updates
   underneath them, and the game then fails to start. A hashed path changes
   whenever the bytes change, so every deploy busts the cache by itself.

2. Writes a responsive full-window index.html (Unity's own template is a small
   fixed-size canvas) plus a vercel.json that caches the hashed build forever
   and the page not at all.

Safe to run repeatedly: if the artifacts are already in a hashed folder it
reuses them rather than treating that folder as stale output.
"""

import hashlib
import json
import os
import shutil
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILD = sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, "Build", "WebGL")
BDIR = os.path.join(BUILD, "Build")

SUFFIXES = {
    "loader": (".loader.js",),
    "data": (".data.unityweb", ".data.br", ".data.gz", ".data"),
    "frame": (".framework.js.unityweb", ".framework.js.br", ".framework.js.gz", ".framework.js"),
    "code": (".wasm.unityweb", ".wasm.br", ".wasm.gz", ".wasm"),
}


def match(directory):
    """Return {role: filename} if this directory holds a complete set, else None."""
    try:
        names = sorted(e for e in os.listdir(directory) if os.path.isfile(os.path.join(directory, e)))
    except OSError:
        return None

    found = {}
    for role, suffixes in SUFFIXES.items():
        for suffix in suffixes:
            hit = next((n for n in names if n.endswith(suffix)), None)
            if hit:
                found[role] = hit
                break
    return found if len(found) == len(SUFFIXES) else None


def locate():
    """Find the artifacts, whether freshly built or already packaged.

    Unity writes them flat into Build/; a previous run of this script will have
    moved them into Build/<hash>/. Both layouts have to be recognised, or a
    second run would delete the build it was meant to package.
    """
    if not os.path.isdir(BDIR):
        sys.exit(f"no Build/ folder under {BUILD} - run the Unity build first")

    fresh = match(BDIR)
    if fresh:
        return BDIR, fresh, True

    for entry in sorted(os.listdir(BDIR)):
        sub = os.path.join(BDIR, entry)
        if os.path.isdir(sub):
            packaged = match(sub)
            if packaged:
                return sub, packaged, False

    sys.exit(f"no complete set of Unity build artifacts found under {BDIR}")


def content_tag(directory, files):
    digest = hashlib.sha1()
    for role in ("loader", "data", "frame", "code"):
        with open(os.path.join(directory, files[role]), "rb") as handle:
            for chunk in iter(lambda: handle.read(1 << 20), b""):
                digest.update(chunk)
    return digest.hexdigest()[:10]


INDEX = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, user-scalable=no">
<title>Meniscus</title>
<link rel="icon" href="data:,">
<style>
  :root { color-scheme: dark; }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { height: 100%; background: #0d0a08; overflow: hidden; }
  body {
    font: 14px/1.5 ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
    color: #c9b89a;
  }
  #wrap { position: fixed; inset: 0; }
  #canvas { display: block; width: 100%; height: 100%; }
  #loading {
    position: fixed; inset: 0;
    display: grid; place-content: center; justify-items: center; gap: 18px;
    background: #0d0a08; transition: opacity .5s ease;
  }
  #loading.done { opacity: 0; pointer-events: none; }
  #title { font-size: 26px; letter-spacing: .34em; text-transform: uppercase; color: #e4d3b0; }
  #bar { width: min(280px, 62vw); height: 2px; background: #2b2119; overflow: hidden; }
  #fill { height: 100%; width: 0; background: #c08a3e; transition: width .2s ease; }
  #hint { font-size: 11px; letter-spacing: .18em; text-transform: uppercase; color: #6b5a44; }
  #error { position: fixed; inset: 0; display: none; place-content: center; padding: 24px; text-align: center; }
</style>
</head>
<body>
<div id="wrap"><canvas id="canvas" tabindex="-1"></canvas></div>

<div id="loading">
  <div id="title">Meniscus</div>
  <div id="bar"><div id="fill"></div></div>
  <div id="hint">Loading</div>
</div>

<div id="error"><div>
  <p style="font-size:16px;color:#e4d3b0;margin-bottom:10px">Meniscus could not start</p>
  <p id="errmsg" style="font-size:12px;color:#8a7663"></p>
</div></div>

<script src="Build/__TAG__/__LOADER__"></script>
<script>
  var canvas  = document.querySelector("#canvas");
  var loading = document.querySelector("#loading");
  var fill    = document.querySelector("#fill");
  var hint    = document.querySelector("#hint");

  var config = {
    dataUrl: "Build/__TAG__/__DATA__",
    frameworkUrl: "Build/__TAG__/__FRAME__",
    codeUrl: "Build/__TAG__/__CODE__",
    __SA__
    companyName: "derbreitesohn",
    productName: "Meniscus",
    productVersion: "1.0",
    matchWebGLToCanvasSize: true,
  };

  // Track the backing store to the real pixel size, so the canvas is not blurry on
  // hidpi screens and reflows correctly on resize or rotate.
  function resize() {
    var dpr = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width  = Math.floor(window.innerWidth  * dpr);
    canvas.height = Math.floor(window.innerHeight * dpr);
  }
  resize();
  window.addEventListener("resize", resize);

  createUnityInstance(canvas, config, function (p) {
    fill.style.width = (p * 100).toFixed(1) + "%";
  }).then(function () {
    loading.classList.add("done");
    setTimeout(function () { loading.style.display = "none"; }, 600);
    canvas.focus();
  }).catch(function (err) {
    hint.textContent = "Failed to load";
    document.querySelector("#error").style.display = "grid";
    document.querySelector("#errmsg").textContent = String(err);
    console.error(err);
  });
</script>
</body>
</html>
"""


def main():
    directory, files, needs_move = locate()
    tag = content_tag(directory, files)
    tagdir = os.path.join(BDIR, tag)

    if needs_move:
        os.makedirs(tagdir, exist_ok=True)
        for role in files:
            shutil.move(os.path.join(directory, files[role]), os.path.join(tagdir, files[role]))
    else:
        # Already packaged. Only rename if the folder does not match its contents.
        if os.path.abspath(directory) != os.path.abspath(tagdir):
            shutil.move(directory, tagdir)

    # Drop every other hashed folder so stale builds are not uploaded alongside this one.
    for entry in os.listdir(BDIR):
        path = os.path.join(BDIR, entry)
        if os.path.isdir(path) and os.path.abspath(path) != os.path.abspath(tagdir):
            shutil.rmtree(path)

    has_sa = os.path.isdir(os.path.join(BUILD, "StreamingAssets"))
    html = (INDEX
            .replace("__TAG__", tag)
            .replace("__LOADER__", files["loader"])
            .replace("__DATA__", files["data"])
            .replace("__FRAME__", files["frame"])
            .replace("__CODE__", files["code"])
            .replace("__SA__", 'streamingAssetsUrl: "StreamingAssets",' if has_sa else ""))
    with open(os.path.join(BUILD, "index.html"), "w", encoding="utf-8") as handle:
        handle.write(html)

    forever = "public, max-age=31536000, immutable"
    revalidate = "public, max-age=0, must-revalidate"
    vercel = {
        "headers": [
            # The hashed path changes whenever the build does, so this is safe to pin.
            {"source": "/Build/(.*)", "headers": [{"key": "Cache-Control", "value": forever}]},
            # The page must always be re-fetched: it points at the current hashed build.
            {"source": "/", "headers": [{"key": "Cache-Control", "value": revalidate}]},
            {"source": "/index.html", "headers": [{"key": "Cache-Control", "value": revalidate}]},
        ]
    }
    with open(os.path.join(BUILD, "vercel.json"), "w", encoding="utf-8") as handle:
        json.dump(vercel, handle, indent=2)

    print(f"packaged Build/{tag}")
    for role in ("loader", "data", "frame", "code"):
        size = os.path.getsize(os.path.join(tagdir, files[role])) / 1e6
        print(f"  {files[role]:<34} {size:8.2f} MB")


if __name__ == "__main__":
    main()
