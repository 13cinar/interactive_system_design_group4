"""
SIMPLE CLIENT FOR SOCKET CLIENT
"""

import socket
import json

HOST = "192.168.0.126"  # The server's hostname or IP address
PORT = 54750;            # The port used by the server

def receive(sock):
    data = sock.recv(1024)
    data = data.decode('utf-8')
    msg = json.loads(data)
    print("Received: ", msg)
    return msg

def send(sock, msg):
	data = json.dumps(msg)
	sock.sendall(data.encode('utf-8'))
	print("Sent: ", msg)


with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
	sock.connect((HOST, PORT))

	while True:
		try:
      
			#Implement the client receives dictionary
			
			msg = receive(sock)
			msg['resp'] = "From Client"
			msg['id'] += 1
			send(sock, msg)
		except KeyboardInterrupt:
			exit()
		except:
			pass