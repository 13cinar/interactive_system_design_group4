"""
SIMPLE CLIENT FOR SOCKET CLIENT
"""

import socket
import json
import numpy as np
import time, os
#HOST = "192.168.0.126"  # The server's(Oculus)  hostname or IP address
#HOST = "172.20.10.3"  # Eren's hotspot
#HOST = "10.90.175.12"  # Salv's hotspot
HOST = "10.141.181.250"  # Salv's hotspot
PORT = 54750;            # The port used by the server
unity_anchors = {}  # {anchor_id: np.array([x,y,z])}
# {anchor_id: aruco_id}  # temporary manual mapping for calibration
pair_map = {
    0: 0,
    1: 1,
    2: 2,
}
def receive(sock):
    data = sock.recv(1024)
    data = data.decode('utf-8')
    try:
        msg = json.loads(data)
        print("Received: ", msg)
        return msg
    except Exception as e:
        print("Error on json.loads: ", e)
        raise e

def send(sock, msg):
	data = json.dumps({"msg": msg}) + '\n'
	sock.sendall(data.encode('utf-8'))
	print("Sent: ", msg)
 
def read_rs_dict(path="output.json"):
    try:
        with open(path, "r") as f:
            d = json.load(f)
        return {int(k): np.array(v, dtype=float) for k, v in d.items()}
    except Exception:
        return {}
    
def build_affine_lstsq(R_pts, U_pts):
    """
    Solve U ≈ T * [R;1] via least squares.
    R_pts, U_pts: (N,3)
    Returns T: (4,4)
    """
    N = R_pts.shape[0]
    A = np.zeros((N*3, 12), dtype=float)
    b = np.zeros((N*3,), dtype=float)
    for i in range(N):
        x, y, z = R_pts[i]
        ux, uy, uz = U_pts[i]
        r = 3*i
        A[r,   0:4]  = [x, y, z, 1]   ; b[r]   = ux
        A[r+1, 4:8]  = [x, y, z, 1]   ; b[r+1] = uy
        A[r+2, 8:12] = [x, y, z, 1]   ; b[r+2] = uz
    t, *_ = np.linalg.lstsq(A, b, rcond=None)  # (12,)
    T = np.eye(4, dtype=float)
    T[0,:4] = t[0:4]
    T[1,:4] = t[4:8]
    T[2,:4] = t[8:12]
    return T

def rms_error(T, R_pts, U_pts):
    R_h   = np.hstack([R_pts, np.ones((R_pts.shape[0],1))])  # (N,4)
    U_pred= (R_h @ T.T)[:, :3]
    err   = np.linalg.norm(U_pts - U_pred, axis=1)
    return np.sqrt(np.mean(err**2)), U_pred

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

def save_rigid_result(src_pts, dst_pts, filename="T_CalibrationMatrix.json"):
    R, t, s = solve_rigid(src_pts, dst_pts)
    result = {
        "R": R.tolist(),
        "t": t.tolist(),
        "s": s
    }
    with open(filename, "w") as f:
        json.dump(result, f, indent=4)
    print(f"Saved rigid transform to {filename}")

def compute_T_if_ready(unity_anchors, rs_now, pair_map):
    R_list, U_list = [], []
    for a_id, r_id in pair_map.items():
        if a_id in unity_anchors and r_id in rs_now:
            U_list.append(unity_anchors[a_id])
            R_list.append(rs_now[r_id])
    if len(R_list) >= 3:
        R = np.vstack(R_list)
        U = np.vstack(U_list)
        T = build_affine_lstsq(R, U)
        err, _ = rms_error(T, R, U)
        #print(f"[Calib] Fitted T. RMS error = {err:.003f} m")
        #np.save("T_CalibrationMatrix.npy",T)
        save_rigid_result(R_list,U_list)
        return T, err
    return None, None

def transform_rs_dict_and_send(sock, T, rs_now):
    for r_id, r_xyz in rs_now.items():
        R_h  = np.array([r_xyz[0], r_xyz[1], r_xyz[2], 1.0], dtype=float)
        Uxyz = (T @ R_h)[:3]
        out  = {
            "resp": "aruco_pose",
            "id": int(r_id),
            "x": float(Uxyz[0]),
            "y": float(Uxyz[1]),
            "z": float(Uxyz[2]),
        }
        send(sock, out)
        

###
# Connection Loop
time.sleep(30) # to allow positioning
with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
	sock.connect((HOST, PORT))
	sock.settimeout(0.05)   # avoid blocking forever on recv; tweak if needed
	T= None
 
	while True:
		try:
      
			#Implement the client receives dictionary
			
			msg = receive(sock)
   			#adding the anchor to the dict when created
			if msg.get('resp')=='anchor_created':
				aid=int(msg['id'])
				unity_anchors[aid] = np.array([msg["x"], msg["y"], msg["z"]], dtype=float)
				print("[Client] Stored Unity anchor:", aid, unity_anchors[aid])
			
		# Read latest RealSense detections
		except KeyboardInterrupt:
			exit()
		except TimeoutError as e:
			print(".", flush=True, end=".")
		except Exception as e:
			print("[Loop] Exception:", repr(e), flush=True)
   
		# 2) Read latest RealSense detections (from maine.py -> output.json)
		rs_now = read_rs_dict("output.json")  # ensure path points to the same folder you run from
		pairs = [a for a, r in pair_map.items() if (a in unity_anchors and r in rs_now)]
		if len(pairs) > 0:
			print("[Pairs] have: ", pairs)
		# 3) Compute T once (or whenever you want to refresh) when ≥3 pairs exist
		if T is None:
			T, err = compute_T_if_ready(unity_anchors, rs_now, pair_map)
			if T is not None:
				print(f"[Calib] Fitted T. RMS error = {err:.3f} m")

        # 4) If calibrated, transform *all* current RS detections and send to Unity
		if T is not None and rs_now:
			transform_rs_dict_and_send(sock, T, rs_now)

        # 5) Small sleep to avoid spamming the socket/CPU
		time.sleep(0.1)