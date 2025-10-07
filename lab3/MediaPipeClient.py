import socket
import json
import cv2
import numpy as np
import pyrealsense2 as rs
from MediaPipe import MediaPipe

'''The server's hostname or IP address'''
#HOST = "172.20.10.4" 
HOST = "10.47.236.133"
'''The port used by the server'''
PORT = 50456

def main():
    mp = MediaPipe()

    # Configure depth and color streams
    pipeline = rs.pipeline()
    config = rs.config()

    # Get device product line for setting a supporting resolution
    pipeline_wrapper = rs.pipeline_wrapper(pipeline)
    pipeline_profile = config.resolve(pipeline_wrapper)
    device = pipeline_profile.get_device()
    device_product_line = str(device.get_info(rs.camera_info.product_line))

    found_rgb = False
    for s in device.sensors:
        if s.get_info(rs.camera_info.name) == 'RGB Camera':
            found_rgb = True
            break
    if not found_rgb:
        print("[main] The demo requires Depth camera with Color sensor")
        exit(0)

    config.enable_stream(rs.stream.depth, 640, 480, rs.format.z16, 30)
    config.enable_stream(rs.stream.color, 640, 480, rs.format.bgr8, 30)

    # Start streaming
    pipeline.start(config)
    # Align Color and Depth
    align_to = rs.stream.color
    align = rs.align(align_to)
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.connect((HOST, PORT))
            sock.setblocking(0)
            while True:
                # Wait for a coherent q,
                # 
                # air of frames: depth and color
                frames = pipeline.wait_for_frames()
                aligned_frames = align.process(frames)
                depth_frame = aligned_frames.get_depth_frame()
                color_frame = aligned_frames.get_color_frame()
                if not  depth_frame or not color_frame:
                    continue


                # Detect skeleton and send it to Unity
                color_image = np.asanyarray(color_frame.get_data())
                detection_results = mp.detect(color_image)
                color_image = mp.draw_landmarks_on_image(color_image, detection_results)
                skeleton_data = mp.skeleton(color_image, detection_results, depth_frame)
                if skeleton_data is not None:
                     send(sock, skeleton_data)

                try:
                    # Receive Message from Client
                    receive(sock)
                except:
                    pass
                # Show images
                cv2.namedWindow('RealSense', cv2.WINDOW_AUTOSIZE)
                cv2.imshow('RealSense', color_image)
                cv2.waitKey(1)
    finally:
        # Stop streaming
        pipeline.stop()

def receive(sock):
	data = sock.recv(1024)
	data = data.decode('utf-8')
	msg = json.loads(data)
	print("Received: ", msg)

def send(sock, msg):
	data = json.dumps(msg) + '\n'
	sock.sendall(data.encode('utf-8'))
	print("Sent: ", msg)

if __name__ == '__main__':
    main()