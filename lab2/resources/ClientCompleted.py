import pyrealsense2 as rs
import numpy as np
import cv2
import socket, json, time, threading

# ------------ network config ------------
HOST = "192.168.50.22"   # Quest's Wi-Fi IP
PORT = 50555             # Same port as Unity server
# ----------------------------------------

def send(sock, msg):
    data = json.dumps(msg) + "\n"   # NDJSON framing
    sock.sendall(data.encode("utf-8"))

# ------------ receive anchors from Unity (background) ------------
unity_anchors = {}   # id -> (x, y, z) in Unity space
def recv_unity(sock):
    buf = b""
    while True:
        chunk = sock.recv(4096)
        if not chunk:
            print("Server closed connection")
            break
        buf += chunk
        while b"\n" in buf:
            line, buf = buf.split(b"\n", 1)
            if not line.strip():
                continue
            try:
                msg = json.loads(line.decode("utf-8"))
            except Exception as e:
                print("Bad JSON from Unity:", e)
                continue
            if msg.get("type") == "anchors":
                for a in msg.get("anchors", []):
                    aid = int(a["id"])
                    p = a["position"]
                    unity_anchors[aid] = (float(p["x"]), float(p["y"]), float(p["z"]))
                print("Unity anchors:", unity_anchors)
# -----------------------------------------------------------------

# ------------ rigid transform (stable) ------------
def solve_rigid(src_pts, dst_pts, allow_scale=True):
    """
    src_pts: Nx3 (RealSense coords)
    dst_pts: Nx3 (Unity coords)
    returns (R, t, s) s.t.  x_u ≈ s * R * x_rs + t
    """
    P = np.asarray(src_pts, float); Q = np.asarray(dst_pts, float)
    if P.shape != Q.shape or P.shape[0] < 3:  # needs >=3
        return None
    cP, cQ = P.mean(axis=0), Q.mean(axis=0)
    P0, Q0 = P - cP, Q - cQ
    H = P0.T @ Q0
    U, S, Vt = np.linalg.svd(H)
    R = Vt.T @ U.T
    # reflection fix
    if np.linalg.det(R) < 0:
        Vt[2, :] *= -1
        R = Vt.T @ U.T
    if allow_scale:
        den = (P0**2).sum()
        s = float(S.sum() / den) if den > 1e-9 else 1.0
    else:
        s = 1.0
    t = cQ - s * (R @ cP)
    return R, t, s

def apply_rigid(R, t, s, xyz):
    v = np.asarray(xyz, float)
    out = s * (R @ v) + t
    return float(out[0]), float(out[1]), float(out[2])
# --------------------------------------------------

# ------------ RealSense + ArUco setup ------------
pipeline = rs.pipeline()
config = rs.config()

pipeline_wrapper = rs.pipeline_wrapper(pipeline)
pipeline_profile = config.resolve(pipeline_wrapper)
device = pipeline_profile.get_device()
found_rgb = any(s.get_info(rs.camera_info.name) == "RGB Camera" for s in device.sensors)
if not found_rgb:
    print("This demo requires a RealSense with RGB sensor.")
    raise SystemExit(0)

config.enable_stream(rs.stream.depth, 640, 480, rs.format.z16, 30)
config.enable_stream(rs.stream.color, 640, 480, rs.format.bgr8, 30)

# Pick the dictionary that matches your printed tags:
# arucoDict = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
arucoDict = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_6X6_50)  # <- keep if your tags are 6x6
arucoParams = cv2.aruco.DetectorParameters()
arucoDetector = cv2.aruco.ArucoDetector(arucoDict, arucoParams)

pipeline.start(config)
align = rs.align(rs.stream.color)
# --------------------------------------------------

# ------------ connect to Unity server ------------
sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.connect((HOST, PORT))
print(f"Connected to {HOST}:{PORT}")

# Start reader for anchors
threading.Thread(target=recv_unity, args=(sock,), daemon=True).start()

# Send a one-off test packet so you can see Unity’s receive path immediately
send(sock, {"type": "aruco_unity",
            "timestamp": time.time(),
            "markers": [{"id": 0, "x": 0.0, "y": 1.0, "z": 1.0}]})
print("TX test aruco_unity (id 0)")
# --------------------------------------------------

# Calibration storage and transform
calib_src, calib_dst = [], []   # lists of 3D points (camera, unity)
RTS = None  # (R, t, s)

try:
    while True:
        frames = pipeline.wait_for_frames()
        aligned_frames = align.process(frames)
        depth_frame = aligned_frames.get_depth_frame()
        color_frame = aligned_frames.get_color_frame()
        if not depth_frame or not color_frame:
            continue

        depth_intrinsics = depth_frame.profile.as_video_stream_profile().intrinsics
        depth_image = np.asanyarray(depth_frame.get_data())
        color_image = np.asanyarray(color_frame.get_data())

        corners, ids, _ = arucoDetector.detectMarkers(color_image)
        markers_out = []

        if ids is not None:
            color_image = cv2.aruco.drawDetectedMarkers(color_image, corners, ids)
            
            for c, marker_id in zip(corners, ids.flatten()):
                pts = c[0]  # (4,2)
                cx = int(np.mean(pts[:, 0])); cy = int(np.mean(pts[:, 1]))
                depth = depth_frame.get_distance(cx, cy)  # meters

                if depth > 0:
                    X, Y, Z = rs.rs2_deproject_pixel_to_point(depth_intrinsics, [cx, cy], depth)
                    markers_out.append({
                        "id": int(marker_id),
                        "pixel_x": cx, "pixel_y": cy,
                        "depth_m": float(depth),
                        "X": float(X), "Y": float(Y), "Z": float(Z),
                    })

                    # Build calibration pairs until RTS is solved
                    if RTS is None and int(marker_id) in unity_anchors:
                        calib_src.append([X, Y, Z])
                        ux, uy, uz = unity_anchors[int(marker_id)]
                        calib_dst.append([ux, uy, uz])
                        if len(calib_src) >= 3:  # rigid needs >=3
                            RTS = solve_rigid(calib_src, calib_dst, allow_scale=True)
                            if RTS is not None:
                                R, t, s = RTS
                                print("Solved rigid R:\n", R)
                                print("t:", t, "s:", s)
                else:
                    markers_out.append({
                        "id": int(marker_id),
                        "pixel_x": cx, "pixel_y": cy,
                        "depth_m": None,
                        "X": None, "Y": None, "Z": None,
                    })

        # ---- send to Unity ----
        if markers_out:
            if RTS is not None:
                R, t, s = RTS
                transformed = []
                for mk in markers_out:
                    if mk["Z"] is None:
                        continue
                    tx, ty, tz = apply_rigid(R, t, s, (mk["X"], mk["Y"], mk["Z"]))
                    transformed.append({"id": mk["id"], "x": tx, "y": ty, "z": tz})
                if transformed:
                    print("TX aruco_unity:", transformed[:2], "..." if len(transformed) > 2 else "")
                    send(sock, {"type": "aruco_unity",
                                "timestamp": time.time(),
                                "markers": transformed})
            else:
                send(sock, {"type": "aruco_frame",
                            "timestamp": time.time(),
                            "markers": markers_out})

        # ---- visualize (optional) ----
        depth_colormap = cv2.applyColorMap(
            cv2.convertScaleAbs(depth_image, alpha=0.03), cv2.COLORMAP_JET
        )
        depth_colormap_dim = depth_colormap.shape
        color_colormap_dim = color_image.shape
        if depth_colormap_dim != color_colormap_dim:
            resized_color_image = cv2.resize(
                color_image,
                dsize=(depth_colormap_dim[1], depth_colormap_dim[0]),
                interpolation=cv2.INTER_AREA,
            )
            images = np.hstack((resized_color_image, depth_colormap))
        else:
            images = np.hstack((color_image, depth_colormap))
        cv2.namedWindow("RealSense", cv2.WINDOW_AUTOSIZE)
        cv2.imshow("RealSense", images)
        cv2.waitKey(1)

except KeyboardInterrupt:
    pass
finally:
    try:
        sock.close()
    except:
        pass
    pipeline.stop()
    cv2.destroyAllWindows()
