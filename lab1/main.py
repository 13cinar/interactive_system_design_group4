## License: Apache 2.0. See LICENSE file in root directory.
## Copyright(c) 2015-2017 Intel Corporation. All Rights Reserved.

###############################################
##      Open CV and Numpy integration        ##
###############################################

import pyrealsense2 as rs
import numpy as np
import cv2

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
    print("The demo requires Depth camera with Color sensor")
    exit(0)

config.enable_stream(rs.stream.depth, 640, 480, rs.format.z16, 30)
config.enable_stream(rs.stream.color, 640, 480, rs.format.bgr8, 30)

#Slide 18
arucoDict = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_1000)
arucoParams = cv2.aruco.DetectorParameters()
arucoDetector = cv2.aruco.ArucoDetector(arucoDict,arucoParams)

# Start streaming
pipeline.start(config)
output_dict = {}
try:
    while True:

        # Wait for a coherent pair of frames: depth and color
        frames = pipeline.wait_for_frames()
        depth_frame = frames.get_depth_frame()
        color_frame = frames.get_color_frame()
        if not depth_frame or not color_frame:
            continue

        # Convert images to numpy arrays
        depth_image = np.asanyarray(depth_frame.get_data())
        color_image = np.asanyarray(color_frame.get_data())
        
        ################################
        # LAB 0 & 1 GETTING MARKERS
        
        corners, ids, reject = arucoDetector.detectMarkers(color_image)
        #print("ids",ids)
        ids_list=np.array(ids)
        #print("rejects",reject)
        color_image = cv2.aruco.drawDetectedMarkers(color_image,corners,ids)

        if len(corners)> 0:
            #print("corners",corners)
            origin_coordinates_list = corners[0][0]
            ids_list=ids[0]
            #print("orig coords", origin_coordinates_list)
            
            for idx, id in enumerate(ids_list):
                #if id in reject:
                #    continue
                
                curr_coord = origin_coordinates_list[idx]
                #calculate 3d
                curr_depth = depth_frame.get_distance(curr_coord[0],curr_coord[1])
                curr_depth_intrinsics = depth_frame.profile.as_video_stream_profile().intrinsics
                curr_3d_coord=rs.rs2_deproject_pixel_to_point(curr_depth_intrinsics,curr_coord,curr_depth)
                # add obj
                newData = {int(id): curr_3d_coord}
                output_dict.update(newData)

        print(output_dict)

        # Apply colormap on depth image (image must be converted to 8-bit per pixel first)
        depth_colormap = cv2.applyColorMap(cv2.convertScaleAbs(depth_image, alpha=0.03), cv2.COLORMAP_JET)

        depth_colormap_dim = depth_colormap.shape
        color_colormap_dim = color_image.shape

        # If depth and color resolutions are different, resize color image to match depth image for display
        if depth_colormap_dim != color_colormap_dim:
            resized_color_image = cv2.resize(color_image, dsize=(depth_colormap_dim[1], depth_colormap_dim[0]), interpolation=cv2.INTER_AREA)
            images = np.hstack((resized_color_image, depth_colormap))
        else:
            images = np.hstack((color_image, depth_colormap))

        # Show images
        cv2.namedWindow('RealSense', cv2.WINDOW_AUTOSIZE)
        cv2.imshow('RealSense', images)
        cv2.waitKey(1)

finally:

    # Stop streaming
    pipeline.stop()

    
