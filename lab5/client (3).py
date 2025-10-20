import openai
import networkx as nx
import os
import json
from pydantic import BaseModel
import matplotlib.pyplot as plt
import mplcursors
import textwrap
import math

API_KEY = os.getenv("OPENAI_API_KEY")
#initialiying the client
def initialize_openai():
    openai.api_key = API_KEY
    return openai.OpenAI()
class Edge(BaseModel):
    source:str
    target:str
    description:str
    
class NodeObstacle(BaseModel):
    title: str
    nodes: list[str]
    edges: list[Edge]
    short_description: str
    
    @staticmethod
    def call_openai(prompt: str, client: openai.OpenAI) -> "NodeObstacle":
        completion = client.chat.completions.parse(
            model="gpt-4o",
            messages = [
                {"role": "system", "content": "You are a puzzle graph generator."},
                {"role": "user", "content": prompt}
            ],
            response_format = NodeObstacle,
            temperature=0.7)
        return completion.choices[0].message.parsed
        
        



    #Making an API call - sending a prompt and optain a response message from the server


# Function to parse the puzzle response from the server, we will store the nodes and edges to iterate over them later
def parse_puzzle_response(puzzle: NodeObstacle) -> str:
    print(puzzle)
    edges_str = "\n".join([f"{edge.source} --({edge.description})-> {edge.target}" for edge in puzzle.edges])
    print(edges_str)
    nodes_str = ", ".join(puzzle.nodes)
    print( nodes_str)
    puzzle_representation = f"Puzzle Name: {puzzle.title}\nNodes: {nodes_str}\nEdges:\n{edges_str}\nDescription: {puzzle.short_description}"
    print (puzzle_representation)
    return puzzle_representation,edges_str,nodes_str
#This returns edges_str: "Bucket --(Use the bucket to collect any leaks that the umbrella might not catch while studying outdoors.)-> Umbrella Hammer --(Use the hammer to fix any loose parts of the umbrella stand.)-> Umbrella" and nodes_str: "Bucket, Hammer, Coffee Cup, Alcohol Spray, Umbrella, Study for an Exam Effectively"

def generate_puzzles(goals, object_set, client):
    puzzles = []
    graphs = []
    for goal in goals:
        prompt = f"""
        Create a puzzle using ALL of these objects: {', '.join(object_set)}.
        The goal is: "{goal}".
        Represent the puzzle as a graph with:
        - Nodes: the objects
        - Edges: interactions or dependencies (e.g., 'use X to modify Y')
        Output format:
        Puzzle name
        Nodes: [...]
        Edges: [...]
        Short description.
        """
        puzzle = NodeObstacle.call_openai(prompt, client)
        puzzle_data, edge_data, node_data = parse_puzzle_response(puzzle)
        graph = create_graph(edge_data, node_data)
        graphs.append(graph)
        puzzles.append(puzzle)
    return puzzles,graphs
#I want to write a function that gets edges_str and nodes_str as input and creates a networkx graph from them.
def create_graph(edges_str: str, nodes_str: str) -> nx.Graph:
    print(edges_str)
    print("----")
    print(nodes_str)
    G = nx.DiGraph()
    
    # Add nodes
    nodes = [node.strip() for node in nodes_str.split(",")]
    G.add_nodes_from(nodes)
    
    # Add edges
    for line in edges_str.split("\n"):
        if line.strip():
            parts = line.split("--(")
            source = parts[0].strip()
            rest = parts[1].split(")->")
            description = rest[0].strip()
            target = rest[1].strip()
            G.add_edge(source, target, description=description)
    
    return G


def visualize_graph(G: nx.DiGraph, layout: str = "spring"): 
    # Choose layout
    if layout == "circular":
        pos = nx.circular_layout(G)
    elif layout == "shell":
        pos = nx.shell_layout(G)
    else:
        pos = nx.spring_layout(G, seed=42)

    # Extract edge labels
    edge_labels = nx.get_edge_attributes(G, 'description')

    plt.figure(figsize=(9, 7))
    
    # Draw nodes with soft edges and a subtle border
    nx.draw_networkx_nodes(
        G, pos,
        node_color="#00E1FF",
        node_size=6000,
        alpha=0.35,
        linewidths=1.5,
        edgecolors="gray"
    )

    # Draw node labels centered within nodes
    nx.draw_networkx_labels(
        G, pos,
        font_size=10,
        font_weight='bold',
        verticalalignment='center',
        horizontalalignment='center'
    )

    # Adjust edge rendering for better arrow visibility
    # arrows=True sometimes draws under nodes, so we offset slightly
    nx.draw_networkx_edges(
        G, pos,
        arrowstyle='-|>',
        arrowsize=18,
        edge_color="gray",
        width=1.5,
        connectionstyle="arc3,rad=0.1",
        min_source_margin=20,   # creates small gap before node
        min_target_margin=20    # ensures arrowhead doesn't overlap node
    )

    # Split long edge descriptions into multiple lines
    wrapped_edge_labels = {}
    for k, v in edge_labels.items():
        wrapped_text = "\n".join(textwrap.wrap(v, width=40))
        wrapped_edge_labels[k] = wrapped_text

    # Draw readable edge labels in dark red, aligned to center
    nx.draw_networkx_edge_labels(
        G, pos,
        edge_labels=wrapped_edge_labels,
        font_color='darkred',
        font_size=7,
        rotate=False,
        label_pos=0.5,
        horizontalalignment='center',
        verticalalignment='center'
    )

    # Add a more prominent and centered title
    plt.title(
        G.graph.get("name", "Puzzle Graph"),
        fontsize=16,
        fontweight='bold',
        pad=20,
        loc='center'
    )

    # Fine-tuned axis settings for a cleaner look
    plt.axis('off')
    plt.tight_layout(pad=2)
    plt.show()

#main pipeline
if __name__ == "__main__":
    client = initialize_openai()

    # Define goals
    goals = [
        "Study for an exam effectively",
        "Cross a river without getting wet",
        "Make a shelter during a rainstorm"
    ]

    # Define object sets
    object_set_1 = ["bucket", "hammer", "coffee cup", "alcohol spray", "umbrella"]
    object_set_2 = ["rope", "flashlight", "book", "apple", "blanket"]

    # Generate puzzles
    print("\n--- PUZZLES WITH OBJECT SET 1 ---\n")
    puzzles1,graphs1 = generate_puzzles(goals, object_set_1, client)
    for p in puzzles1:
        print(p, "\n")
    for g in graphs1:
        print(g, "\n")
        visualize_graph(g)

    print("\n--- PUZZLES WITH OBJECT SET 2 ---\n")
    puzzles2,graphs2 = generate_puzzles(goals, object_set_2, client)
    for p in puzzles2:
        print(p, "\n")
    for g in graphs2:
        print(g, "\n")
        visualize_graph(g)