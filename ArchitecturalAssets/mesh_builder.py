import os

models_dir = r"d:\PathFinder\PathfinderAR\Assets\Models\Campus"
os.makedirs(models_dir, exist_ok=True)

class MeshBuilder:
    def __init__(self, name):
        self.name = name
        self.vertices = []
        self.normals = []
        self.uvs = []
        self.faces = [] # 1-indexed for OBJ, 0-indexed for FBX

    def add_box(self, center, size, name=None):
        cx, cy, cz = center
        sx, sy, sz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
        
        # 8 corners
        v_start = len(self.vertices)
        corners = [
            (cx - sx, cy - sy, cz - sz), # 0
            (cx + sx, cy - sy, cz - sz), # 1
            (cx + sx, cy + sy, cz - sz), # 2
            (cx - sx, cy + sy, cz - sz), # 3
            (cx - sx, cy - sy, cz + sz), # 4
            (cx + sx, cy - sy, cz + sz), # 5
            (cx + sx, cy + sy, cz + sz), # 6
            (cx - sx, cy + sy, cz + sz), # 7
        ]
        self.vertices.extend(corners)
        
        # 6 quad faces -> 12 triangles
        quads = [
            (0, 3, 2, 1), # South (-Z)
            (5, 6, 7, 4), # North (+Z)
            (4, 7, 3, 0), # West (-X)
            (1, 2, 6, 5), # East (+X)
            (3, 7, 6, 2), # Top (+Y)
            (4, 0, 1, 5), # Bottom (-Y)
        ]
        
        for q in quads:
            # 2 triangles per quad
            self.faces.append((v_start + q[0], v_start + q[1], v_start + q[2]))
            self.faces.append((v_start + q[0], v_start + q[2], v_start + q[3]))

    def write_obj(self, filepath, material_name="CampusWall"):
        with open(filepath, "w", encoding="utf-8") as f:
            f.write(f"# PathFinder Campus 3D Model: {self.name}\n")
            f.write(f"mtllib campus_materials.mtl\n")
            f.write(f"o {self.name}\n")
            for v in self.vertices:
                f.write(f"v {v[0]:.4f} {v[1]:.4f} {v[2]:.4f}\n")
            f.write(f"usemtl {material_name}\n")
            f.write("s 1\n")
            for face in self.faces:
                # 1-indexed for OBJ
                f.write(f"f {face[0]+1} {face[1]+1} {face[2]+1}\n")

    def write_fbx_ascii(self, filepath):
        # Generates standard FBX 7.4 ASCII file format natively parsed by Unity
        with open(filepath, "w", encoding="utf-8") as f:
            f.write('; FBX 7.4.0 project file\n')
            f.write('FBXHeaderExtension:  {\n')
            f.write('    FBXHeaderVersion: 1003\n')
            f.write('    FBXVersion: 7400\n')
            f.write('    Creator: "PathFinder BIM 3D Generator"\n')
            f.write('}\n')
            f.write('GlobalSettings:  {\n')
            f.write('    Version: 1000\n')
            f.write('    Properties70:  {\n')
            f.write('        P: "UpAxis", "int", "Integer", "",1\n')
            f.write('        P: "UpAxisSign", "int", "Integer", "",1\n')
            f.write('        P: "FrontAxis", "int", "Integer", "",2\n')
            f.write('        P: "FrontAxisSign", "int", "Integer", "",1\n')
            f.write('        P: "CoordAxis", "int", "Integer", "",0\n')
            f.write('        P: "CoordAxisSign", "int", "Integer", "",1\n')
            f.write('        P: "UnitScaleFactor", "double", "Number", "",100.0\n')
            f.write('    }\n')
            f.write('}\n')
            f.write('Objects:  {\n')
            geom_id = 1000001
            model_id = 2000001
            f.write(f'    Geometry: {geom_id}, "Geometry::{self.name}_Mesh", "Mesh" {{\n')
            
            # Vertices
            flat_verts = []
            for v in self.vertices:
                flat_verts.extend([v[0]*100.0, v[1]*100.0, v[2]*100.0])
            f.write(f'        Vertices: *{len(flat_verts)} {{\n')
            f.write(f'            a: {",".join(f"{x:.2f}" for x in flat_verts)}\n')
            f.write('        }\n')
            
            # PolygonVertexIndex
            # In FBX, the last index of each polygon is bitwise inverted: ~index = -(index + 1)
            poly_indices = []
            for face in self.faces:
                poly_indices.append(str(face[0]))
                poly_indices.append(str(face[1]))
                poly_indices.append(str(-(face[2] + 1)))
            f.write(f'        PolygonVertexIndex: *{len(poly_indices)} {{\n')
            f.write(f'            a: {",".join(poly_indices)}\n')
            f.write('        }\n')
            f.write('    }\n')
            
            f.write(f'    Model: {model_id}, "Model::{self.name}", "Mesh" {{\n')
            f.write('        Version: 232\n')
            f.write('        Properties70:  {\n')
            f.write('            P: "InheritType", "enum", "", "",1\n')
            f.write('        }\n')
            f.write('    }\n')
            f.write('}\n')
            
            # Connections
            f.write('Connections:  {\n')
            f.write(f'    ;Model::{self.name}, Geometry::{self.name}_Mesh\n')
            f.write(f'    C: "OO",{geom_id},{model_id}\n')
            f.write(f'    ;Model::{self.name}, Model::RootNode\n')
            f.write(f'    C: "OO",{model_id},0\n')
            f.write('}\n')

# Create Material MTL file
mtl_path = os.path.join(models_dir, "campus_materials.mtl")
with open(mtl_path, "w", encoding="utf-8") as f:
    f.write("""# PathFinder Campus Materials
newmtl CampusWall
Kd 0.88 0.90 0.94
Ka 0.20 0.22 0.25
Ks 0.10 0.10 0.10
Ns 10.0
d 1.0

newmtl CampusFloor
Kd 0.35 0.38 0.42
Ka 0.15 0.15 0.15
Ks 0.15 0.15 0.15
Ns 20.0
d 1.0

newmtl AdminAccent
Kd 0.15 0.55 0.92
Ka 0.10 0.25 0.45
Ks 0.40 0.40 0.40
Ns 30.0
d 1.0

newmtl TeachingAccent
Kd 0.25 0.75 0.50
Ka 0.10 0.35 0.20
Ks 0.30 0.30 0.30
Ns 25.0
d 1.0
""")

print("Initialized MeshBuilder and Material templates.")
