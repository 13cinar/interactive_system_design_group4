import pyrealsense2 as rs
import numpy as np
import cv2
import socket, json, time, threading
from MediaPipe import MediaPipe
import time

# ------------ network config ------------
HOST = "127.0.0.1"   # Quest's Wi-Fi IP
PORT = 13456             # Same port as Unity server
# ----------------------------------------

CART_MARKER_ID = 5
REFILL_ID = 2
THRESH=5
_visible_streak, _hidden_streak = 0, 0

def send(sock, msg):
    data = json.dumps(msg) + "\n"   # NDJSON framing
    sock.sendall(data.encode("utf-8"))
    
def send_refill(sock, need: bool):
    send(sock, {"type":"refill_signal", "value": "NEED_REFILL" if need else "REFILL_DONE"})

# ------------ receive anchors from Unity (background) ------------
unity_anchors = {}   # id -> (x, y, z) in Unity space
# {
    # 0: (0.0, 0.0, 0.0), id 0 -> head
    # 1: (1.0, 0.0, 0.0),
    # 2: (0.0, 1.0, 0.0)
# }

# receives and update unity_anchors dictionary
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
            
            
            
            # {type: "anchors", anchors: [ {id: 0, position: {x: 0.0, y: 0.0, z: 0.0}}, ... ] }
            # print("Received from Unity:", msg)
            if msg.get("type") == "anchors":
                for anchor in msg.get("anchors", []):
                    anchor_id = int(anchor["id"])
                    unity_anchors[anchor_id] = (float(anchor["X"]), float(anchor["Y"]), float(anchor["Z"]))
                # print("Unity anchors:", unity_anchors)

# -----------------------------------------------------------------

# ------------ rigid transform (stable) ------------
def solve_rigid(src_pts, dst_pts, allow_scale=True):
    """
    src_pts: Nx3 (mediapipe coords)
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
    # out = v + t
    return float(out[0]), float(out[1]), float(out[2])
# --------------------------------------------------

# ------------ RealSense + ArUco setup ------------
mediaPipe = MediaPipe()

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

# ------------ connect to Unity `s`erver ------------
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
        
        detection_results = mediaPipe.detect(color_image)
        color_image = mediaPipe.draw_landmarks_on_image(color_image, detection_results)
        skeleton_data = mediaPipe.skeleton(color_image, detection_results, depth_frame)
        
        # lHand_x, lHand_y, lHand_z, rHand_x, rHand_y, rHand_z, Head_x, Head_y, Head_z = skeleton_data
        # if skeleton_data is not None:
        #     send(sock, skeleton_data)

        # corners, ids, _ = arucoDetector.detectMarkers(color_image)
        transformed_positions = []
        
        landmark_to_id_map = {
            0: "Head",
            1: "LHand",
            2: "RHand"
        }

        if skeleton_data is not None:
            # print("SKELETON DATA:", skeleton_data)
            lHand_x, lHand_y, lHand_z, rHand_x, rHand_y, rHand_z, Head_x, Head_y, Head_z = skeleton_data
            # Build calibration pairs until RTS is solved
            for marker_id, position in unity_anchors.items():
                # 0, 1, 2 are head, left hand, right hand
                if RTS is None:
                    X, Y, Z = position
                    calib_dst.append([X, Y, Z])
                    # calib_src # [[mediapipe position head x, y, z], [mediapipe position left hand x, y, z], [mediapipe position right hand x, y, z]]
                    # calib_dst # [[unity position head x, y, z], [unity position left hand x, y, z], [unity position right hand x, y, z]]
                    calib_src.append([skeleton_data[landmark_to_id_map[marker_id] + "_x"],
                                     skeleton_data[landmark_to_id_map[marker_id] + "_y"],
                                     skeleton_data[landmark_to_id_map[marker_id] + "_z"]])
                    if len(calib_src) >= 3:  # rigid needs >=3
                        RTS = solve_rigid(calib_src, calib_dst, allow_scale=True)
                    if RTS is not None:
                        R, t, s = RTS
                        print("Solved rigid R:\n", R)
                        print("t:", t, "s:", s)
                else:
                    transformed_X, transformed_Y, transformed_Z = apply_rigid(R, t, s, (skeleton_data[landmark_to_id_map[marker_id] + "_x"],
                                                    skeleton_data[landmark_to_id_map[marker_id] + "_y"],
                                                    skeleton_data[landmark_to_id_map[marker_id] + "_z"]))
                    # print(transformed_X, transformed_Y, transformed_Z)
                    transformed_positions.append({"id": marker_id,
                                        "x": transformed_X, "y": transformed_Y, "z": transformed_Z})
                    # print(transformed_positions)
         # -------- NEW: detect the cart marker (ArUco id=5) and send it --------
        # Only after RTS is solved (we need the calibration to convert camera->Unity)
        if RTS is not None:
            corners, ids, _ = arucoDetector.detectMarkers(color_image)
            cv2.aruco.drawDetectedMarkers(color_image, corners, ids)
            saw_refill= False
            if ids is not None and len(ids) > 0:
                ids = ids.flatten()
                for c, mid in zip(corners, ids):
                    if int(mid) == CART_MARKER_ID:
                        pts = c[0]  # (4,2)
                        cx = int(np.mean(pts[:, 0])); cy = int(np.mean(pts[:, 1]))
                        depth = depth_frame.get_distance(cx, cy)  # meters
                        if depth > 0:
                            X, Y, Z = rs.rs2_deproject_pixel_to_point(depth_intrinsics, [cx, cy], depth)
                            R, t, s = RTS
                            tx, ty, tz = apply_rigid(R, t, s, (X, Y, Z))
                            transformed_positions.append({
                                "id": CART_MARKER_ID,
                                "x": tx, "y": ty, "z": tz
                        })
                    elif int(mid)== REFILL_ID:
                        saw_refill = True
        # ---- send to Unity ----
            if transformed_positions:
                send(sock, {"type": "aruco_unity",
                            "timestamp": time.time(),
                            "markers": transformed_positions})
                transformed_positions = []
                
            if saw_refill:
                _visible_streak += 1
                _hidden_streak = 0
                if _visible_streak == THRESH:
                    print("[REFILL] NEED_REFILL")
                    send(sock, {"type": "refill_signal", "value": "NEED_REFILL"})
            else:
                _hidden_streak += 1
                _visible_streak = 0
                if _hidden_streak == THRESH:
                    print("[REFILL] REFILL_DONE")
                    send(sock, {"type": "refill_signal", "value": "REFILL_DONE"})
                
            time.sleep(0.05)  # slight delay to avoid flooding

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
