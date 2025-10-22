import openai
import networkx as nx
import os
import json
from pydantic import BaseModel
import matplotlib.pyplot as plt
import textwrap
import base64

API_KEY = os.getenv("OPENAI_API_KEY")
def initialize_openai():
    openai.api_key = API_KEY
    return openai.OpenAI()

class Edge(BaseModel):
    source: str
    target: str
    description: str

class NodeObstacle(BaseModel):
    title: str
    nodes: list[str]
    edges: list[Edge]
    short_description: str

    @staticmethod
    def call_openai(prompt: str, client: openai.OpenAI) -> "NodeObstacle":
        completion = client.chat.completions.parse(
            model="gpt-4o",
            messages=[
                {"role": "system", "content": "You are a puzzle graph generator."},
                {"role": "user", "content": prompt}
            ],
            response_format=NodeObstacle,
            temperature=0.7
        )
        return completion.choices[0].message.parsed

#gets the objects from an
class ObjectNames(BaseModel):
    objects: list[str]

def _extract_objects_with_vision(client, content_blocks) -> list[str]:
    """
    Calls GPT-4o vision with provided 'messages[...].content' blocks and parses to list[str].
    """
    prompt = (
        "From the image, list 5–10 DISTINCT, VISIBLE objects as short singular names. "
        "Return STRICT JSON with one field 'objects': string[]. No extra text, no comments."
    )
    resp = client.chat.completions.parse(
        model="gpt-4o-mini",  
        messages=[
            {"role": "system", "content": "You extract concise object names for a puzzle generator."},
            {"role": "user", "content": [{"type": "text", "text": prompt}, 
            *content_blocks],},
        ],
        response_format=ObjectNames,
        temperature=0.2
    )
    names = resp.choices[0].message.parsed.objects
    cleaned = []
    for n in names:
        n = n.strip()
        n = n.lower()
        if n and n not in cleaned:
            cleaned.append(n)
    return cleaned

def get_objects_from_image_url(client, image_url: str) -> list[str]:
    """
    Use a public HTTPS image URL.
    """
    content_blocks = [
        {"type": "image_url", "image_url": {"url": image_url}}
    ]
    return _extract_objects_with_vision(client, content_blocks)

def get_objects_from_local_image(client, image_path: str) -> list[str]:
    """
    Use a local file; encodes as base64 data URL so GPT can see it.
    """
    with open(image_path, "rb") as f:
        b64 = base64.b64encode(f.read()).decode("utf-8")
    mime = "image/png" if image_path.lower().endswith(".png") else "image/jpeg"
    data_url = f"data:{mime};base64,{b64}"
    content_blocks = [
        {"type": "image_url", "image_url": {"url": data_url}}
    ]
    return _extract_objects_with_vision(client, content_blocks)

#same as the other client
def parse_puzzle_response(puzzle: NodeObstacle):
    print(puzzle)
    edges_str = "\n".join([f"{e.source} --({e.description})-> {e.target}" for e in puzzle.edges])
    print(edges_str)
    nodes_str = ", ".join(puzzle.nodes)
    print(nodes_str)
    puzzle_representation = (
        f"Puzzle Name: {puzzle.title}\n"
        f"Nodes: {nodes_str}\n"
        f"Edges:\n{edges_str}\n"
        f"Description: {puzzle.short_description}"
    )
    print(puzzle_representation)
    return puzzle_representation, edges_str, nodes_str

def generate_puzzles(goals, object_set, client):
    puzzles, graphs = [], []
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
        _, edge_data, node_data = parse_puzzle_response(puzzle)
        puzzles.append(puzzle)
    return puzzles, graphs


#maın
if __name__ == "__main__":
    client = initialize_openai()

    # Goals (same)
    goals = [
        "Study for an exam effectively",
        "Cross a river without getting wet",
        "Make a shelter during a rainstorm"
    ]
    local_path = os.path.dirname(__file__)
    file_to_access= os.path.join(local_path,"pictureelsa.png")
    object_set_from_image = get_objects_from_local_image(client, file_to_access)

    print("\n[OBJECTS FROM IMAGE]")
    print(object_set_from_image)

    print("\n--- PUZZLES WITH OBJECTS FROM IMAGE ---\n")
    puzzles_img, graphs_img = generate_puzzles(goals, object_set_from_image, client)
    for p in puzzles_img:
        print(p, "\n")
