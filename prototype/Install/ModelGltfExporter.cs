using System.Buffers.Binary;
using Reforged.Formats;
using static Reforged.Install.ModelGltf;

namespace Reforged.Install;

/// <summary>lms2gltf.Exporter: accumulates glTF nodes, meshes, materials, skins and animations in the Python's order,
/// so indices and buffer contents come out the same.</summary>
internal sealed class Exporter(ModelResolver resolver)
{
    private readonly List<byte> _bin = [];
    public readonly List<Dictionary<string,object?>> Views = [], Accessors = [], Nodes = [], Meshes = [], Materials = [], Textures = [], Images = [],
        Skins = [], Animations = [];
    public readonly List<object?> Scene = [];
    public readonly List<string> Log = [];
    public Dictionary<string,object?> Appearance = [];
    public Dictionary<string,object?> Extras = [];
    private readonly Dictionary<(string?,string?,bool),int> _materialFor = [];
    private readonly Dictionary<string,(int Index,bool Alpha)> _textureFor = [];
    private readonly Dictionary<string,DecodedImage> _decoded = [];
    private readonly Dictionary<(LskSkeleton,int),int> _frames = [];
    private readonly Dictionary<int,(int,int,int,byte[])> _imagePixels = [];

    // ---- Buffers ------------------------------------------------------------------------------------------------

    private int View(byte[] data,int? target = null)
    {
        while (_bin.Count%4 != 0) _bin.Add(0);
        var view = new Dictionary<string,object?> { ["buffer"] = 0, ["byteOffset"] = _bin.Count, ["byteLength"] = data.Length };
        if (target != null) view["target"] = target;
        _bin.AddRange(data);
        Views.Add(view);
        return Views.Count-1;
    }

    /// <summary>Float rows (5126) packed as 32-bit floats; min and max, when asked, are the Python's first smallest and
    /// largest of the double values (their own lists when given, for rows that mix ints in).</summary>
    public int Floats(IReadOnlyList<double[]> rows,string type,int? target = null,bool minmax = false,List<object?>? min = null,List<object?>? max = null)
    {
        int width = rows.Count > 0 ? rows[0].Length : 0;
        var data = new byte[rows.Count*width*4];
        for (int r = 0, o = 0; r < rows.Count; r++)
            for (int k = 0; k < width; k++, o += 4) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(o),(float)rows[r][k]);
        var accessor = new Dictionary<string,object?> { ["bufferView"] = View(data,target), ["componentType"] = 5126, ["count"] = rows.Count, ["type"] = type };
        if (minmax)
        {
            if (min == null || max == null)
            {
                min = []; max = [];
                for (int k = 0; k < width; k++)
                {
                    double low = rows[0][k], high = rows[0][k];
                    foreach (var row in rows) { if (row[k] < low) low = row[k]; if (row[k] > high) high = row[k]; }
                    min.Add(low); max.Add(high);
                }
            }
            accessor["min"] = min; accessor["max"] = max;
        }
        Accessors.Add(accessor);
        return Accessors.Count-1;
    }

    /// <summary>Build-210: unsigned int values (5125), the scene's index lists.</summary>
    public int Uints(IReadOnlyList<int> values,string type = "SCALAR",int? target = null)
    {
        var data = new byte[values.Count*4];
        for (int i = 0; i < values.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4*i),checked((uint)values[i]));
        Accessors.Add(new() { ["bufferView"] = View(data,target), ["componentType"] = 5125, ["count"] = values.Count, ["type"] = type });
        return Accessors.Count-1;
    }

    /// <summary>Build-210: another file's binary chunk appended at a 4-byte boundary (wor2gltf.import_model); returns
    /// its offset in this buffer.</summary>
    public int Append(byte[] binary)
    {
        while (_bin.Count%4 != 0) _bin.Add(0);
        int offset = _bin.Count;
        _bin.AddRange(binary);
        return offset;
    }

    /// <summary>Build-210: the decoded pixels of an image another file brought in, for the checks.</summary>
    public void ImportedPixels(int image,(int,int,int,byte[]) pixels) => _imagePixels[image] = pixels;

    /// <summary>Build-210: a scene has no wotrAppearance (lms2gltf.Exporter writes it only when a model set one).</summary>
    public bool WriteAppearance = true;

    /// <summary>Unsigned short rows (5123): indices or joint quadruples.</summary>
    public int Shorts(IReadOnlyList<int> values,int perRow,string type,int? target = null)
    {
        var data = new byte[values.Count*2];
        for (int i = 0; i < values.Count; i++) BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2*i),checked((ushort)values[i]));
        Accessors.Add(new() { ["bufferView"] = View(data,target), ["componentType"] = 5123, ["count"] = values.Count/perRow, ["type"] = type });
        return Accessors.Count-1;
    }

    // ---- Scene pieces -------------------------------------------------------------------------------------------

    public int Node(Dictionary<string,object?> node,int? parent = null)
    {
        Nodes.Add(node);
        int index = Nodes.Count-1;
        if (parent == null) Scene.Add(index);
        else
        {
            if (Nodes[parent.Value].GetValueOrDefault("children") is not List<object?> children) Nodes[parent.Value]["children"] = children = [];
            children.Add(index);
        }
        return index;
    }

    private DecodedImage Picture(string path)
    {
        if (!_decoded.TryGetValue(path,out var image)) _decoded[path] = image = Reforged.Formats.Images.Decode(resolver.Read(path),path);
        return image;
    }

    /// <summary>An image embedded once as PNG: (texture index, has alpha). Alpha is kept when asked and some texel is
    /// not opaque.</summary>
    public (int Index,bool Alpha) Texture(string path,bool keepAlpha = true)
    {
        if (!_textureFor.TryGetValue(path,out var known))
        {
            var image = Picture(path);
            bool alpha = keepAlpha && image.AlphaChannel && image.MinimumAlpha() < 255;
            byte[] pixels = alpha ? image.Rgba : image.Rgb();
            Images.Add(new() { ["bufferView"] = View(Reforged.Formats.Images.Png(image.Width,image.Height,pixels,alpha)), ["mimeType"] = "image/png" });
            _imagePixels[Images.Count-1] = (image.Width,image.Height,alpha ? 4 : 3,pixels);
            Textures.Add(new() { ["source"] = Images.Count-1, ["sampler"] = 0 });
            _textureFor[path] = known = (Textures.Count-1,alpha);
        }
        return known;
    }

    /// <summary>Whether most triangles sample partly transparent texels (8 &lt; alpha &lt; 247): a soft overlay to blend.</summary>
    private bool Translucent(string path,List<double[]> uv,List<int[]> tris)
    {
        if (tris.Count == 0) return false;
        var image = Picture(path);
        if (!image.AlphaChannel) return false;
        int w = image.Width, h = image.Height, partial = 0;
        foreach (var t in tris)
        {
            double u = 0.0+uv[t[0]][0]+uv[t[1]][0]+uv[t[2]][0], v = 0.0+uv[t[0]][1]+uv[t[1]][1]+uv[t[2]][1];
            u /= 3; v /= 3;
            if (!double.IsFinite(u) || !double.IsFinite(v)) continue;
            double tu = Math.Truncate(u*w), tv = Math.Truncate(v*h);
            int x = (int)((tu%w+w)%w), y = (int)((tv%h+h)%h);
            int a = image.Rgba[(y*w+x)*4+3];
            if (a is > 8 and < 247) partial++;
        }
        return partial*2 > tris.Count;
    }

    public int Material(string? texture,string? mask = null,bool blend = false)
    {
        if (_materialFor.TryGetValue((texture,mask,blend),out int known)) return known;
        Dictionary<string,object?> material;
        if (texture != null)
        {
            var (index,alpha) = Texture(texture);
            material = new()
            {
                ["name"] = Path.GetFileNameWithoutExtension(texture),
                ["pbrMetallicRoughness"] = new Dictionary<string,object?> { ["baseColorTexture"] = new Dictionary<string,object?> { ["index"] = index }, ["metallicFactor"] = 0.0, ["roughnessFactor"] = 0.9 },
            };
            if (alpha && blend) { material["name"] = material["name"]+" blend"; material["alphaMode"] = "BLEND"; material["doubleSided"] = true; }
            else if (alpha) { material["alphaMode"] = "MASK"; material["alphaCutoff"] = 0.5; material["doubleSided"] = true; }
            if (mask != null)
            {
                material["name"] = material["name"]+" team";
                material["emissiveTexture"] = new Dictionary<string,object?> { ["index"] = Texture(mask,keepAlpha: false).Index };
                material["emissiveFactor"] = new List<object?> { SrgbLinear(185),SrgbLinear(0),SrgbLinear(0) };
            }
        }
        else material = new()
        {
            ["name"] = "untextured",
            ["pbrMetallicRoughness"] = new Dictionary<string,object?> { ["baseColorFactor"] = new List<object?> { 0.6,0.6,0.6,1.0 }, ["metallicFactor"] = 0.0, ["roughnessFactor"] = 0.9 },
        };
        Materials.Add(material);
        return _materialFor[(texture,mask,blend)] = Materials.Count-1;
    }

    private static double SrgbLinear(int channel)
    {
        double c = channel/255.0;
        return c <= 0.04045 ? c/12.92 : Math.Pow((c+0.055)/1.055,2.4);
    }

    // ---- Meshes -------------------------------------------------------------------------------------------------

    /// <summary>lms2gltf.flatten: render vertices expanded, winding chosen to agree with the stored normals, zero normals
    /// rebuilt from the adjacent triangles (the up axis when none), every normal made unit length.</summary>
    private static (List<double[]> Pos,List<double[]> Nrm,List<double[]> Uv,List<int[]> Tris) Flatten(LmsMesh m)
    {
        var pos = m.PositionIndex.Select(i => Axes(m.Positions[3*i],m.Positions[3*i+1],m.Positions[3*i+2])).ToList();
        var nrm = m.NormalIndex.Select(i => Axes(m.Normals[3*i],m.Normals[3*i+1],m.Normals[3*i+2])).ToList();
        var uv = m.UvIndex.Select(i => new double[] { m.Uvs[2*i],m.Uvs[2*i+1] }).ToList();
        var tris = m.Triangles.Chunk(3).Select(t => t.ToArray()).ToList();
        static double[] Cross(double[] a,double[] b,double[] c)
        {
            double[] e1 = [b[0]-a[0],b[1]-a[1],b[2]-a[2]], e2 = [c[0]-a[0],c[1]-a[1],c[2]-a[2]];
            return [e1[1]*e2[2]-e1[2]*e2[1],e1[2]*e2[0]-e1[0]*e2[2],e1[0]*e2[1]-e1[1]*e2[0]];
        }
        int agree = 0;
        foreach (var t in tris)
        {
            var fn = Cross(pos[t[0]],pos[t[1]],pos[t[2]]);
            double[] vn = [nrm[t[0]][0]+nrm[t[1]][0]+nrm[t[2]][0],nrm[t[0]][1]+nrm[t[1]][1]+nrm[t[2]][1],nrm[t[0]][2]+nrm[t[1]][2]+nrm[t[2]][2]];
            double dot = 0.0+fn[0]*vn[0]+fn[1]*vn[1]+fn[2]*vn[2];
            agree += dot > 0 ? 1 : dot < 0 ? -1 : 0;
        }
        if (agree < 0) tris = [.. tris.Select(t => new[] { t[0],t[2],t[1] })];
        var missing = Enumerable.Range(0,nrm.Count).Where(i => nrm[i].All(c => c == 0)).ToHashSet();
        if (missing.Count > 0)
        {
            var accumulated = missing.ToDictionary(i => i,_ => new double[3]);
            foreach (var t in tris)
            {
                var normal = Cross(pos[t[0]],pos[t[1]],pos[t[2]]);
                foreach (int vertex in t)
                    if (accumulated.TryGetValue(vertex,out var sum)) for (int k = 0; k < 3; k++) sum[k] += normal[k];
            }
            foreach (var (vertex,normal) in accumulated) nrm[vertex] = normal.Any(c => c != 0) ? normal : [0.0,1.0,0.0];
        }
        for (int i = 0; i < nrm.Count; i++)
        {
            var n = nrm[i];
            double length = Math.Sqrt(0.0+n[0]*n[0]+n[1]*n[1]+n[2]*n[2]);
            nrm[i] = [n[0]/length,n[1]/length,n[2]/length];
        }
        return (pos,nrm,uv,tris);
    }

    /// <summary>lms2gltf.vertex_weights: the top four influences per render vertex, normalised.</summary>
    private static (List<int> Joints,List<double[]> Values) VertexWeights(LmsMesh m,LwtWeights weights)
    {
        if (weights.Influences.Length != m.Positions.Length/3)
            throw new InvalidDataException($"{m.Name}: {weights.Influences.Length} weight entries for {m.Positions.Length/3} positions");
        var joints = new List<int>();
        var values = new List<double[]>();
        foreach (uint i in m.PositionIndex)
        {
            var top = weights.Influences[i].OrderBy(jw => -(double)jw.Weight).Take(4).ToList();
            double total = 0.0;
            foreach (var jw in top) total += jw.Weight;
            if (total == 0) total = 1.0;
            while (top.Count < 4) top.Add((0,0f));
            joints.AddRange(top.Select(jw => (int)jw.Joint));
            values.Add([.. top.Select(jw => jw.Weight/total)]);
        }
        return (joints,values);
    }

    /// <summary>lms2gltf.Exporter.mesh: skinned only when the mesh is flagged skinned and has weights.</summary>
    public void Mesh(LmsMesh m,string? texture,string? weightsPath = null,int? skin = null,int jointCount = 0,int? parent = null,string? mask = null)
    {
        var (pos,nrm,uv,tris) = Flatten(m);
        var attributes = new Dictionary<string,object?>
        {
            ["POSITION"] = Floats(pos,"VEC3",34962,minmax: true), ["NORMAL"] = Floats(nrm,"VEC3",34962), ["TEXCOORD_0"] = Floats(uv,"VEC2",34962),
        };
        var node = new Dictionary<string,object?> { ["name"] = m.Name, ["mesh"] = Meshes.Count };
        if (weightsPath != null && skin != null && m.Skinned)
        {
            var weights = LwtWeights.Parse(resolver.Read(weightsPath));
            if (weights.JointCount != jointCount) throw new InvalidDataException($"{weightsPath}: {weights.JointCount} joints, skeleton has {jointCount}");
            var (joints,values) = VertexWeights(m,weights);
            attributes["JOINTS_0"] = Shorts(joints,4,"VEC4",34962);
            attributes["WEIGHTS_0"] = Floats(values,"VEC4",34962);
            node["skin"] = skin;
            parent = null;
        }
        int indices = Shorts([.. tris.SelectMany(t => t)],1,"SCALAR",34963);
        bool blend = texture != null && Translucent(texture,uv,tris);
        int material = Material(texture,mask,blend);
        Meshes.Add(new()
        {
            ["name"] = m.Name,
            ["primitives"] = new List<object?> { new Dictionary<string,object?> { ["attributes"] = attributes, ["indices"] = indices, ["material"] = material } },
        });
        Node(node,parent);
    }

    // ---- Skeletons ----------------------------------------------------------------------------------------------

    /// <summary>Joint nodes, a skin and one animation per clip: (joint nodes, skin).</summary>
    public (List<int> Joints,int Skin) Skeleton(LskSkeleton skel,int? parent,string clipPrefix)
    {
        int first = Nodes.Count;
        foreach (var j in skel.Joints) Nodes.Add(new() { ["name"] = j.Name, ["translation"] = Doubles(Axes(j.Offset[0],j.Offset[1],j.Offset[2])) });
        for (int i = 0; i < skel.Joints.Length; i++)
        {
            int jointParent = skel.Joints[i].Parent;
            if (jointParent < 0)
            {
                if (parent == null) Scene.Add(first+i);
                else ChildOf(parent.Value).Add(first+i);
            }
            else ChildOf(first+jointParent).Add(first+i);
        }
        var joints = Enumerable.Range(first,skel.Joints.Length).ToList();
        var world = new List<double[]>();
        foreach (var j in skel.Joints)
        {
            var offset = Axes(j.Offset[0],j.Offset[1],j.Offset[2]);
            world.Add(j.Parent < 0 ? offset : [world[j.Parent][0]+offset[0],world[j.Parent][1]+offset[1],world[j.Parent][2]+offset[2]]);
        }
        var ibm = world.Select(p => new double[] { 1,0,0,0,0,1,0,0,0,0,1,0,-p[0],-p[1],-p[2],1 }).ToList();
        Skins.Add(new() { ["joints"] = Ints(joints), ["inverseBindMatrices"] = Floats(ibm,"MAT4") });
        foreach (var clip in skel.Clips)
        {
            var samplers = new List<object?>();
            var channels = new List<object?>();
            for (int j = 0; j < Math.Min(joints.Count,clip.Tracks.Length); j++)
            {
                var track = clip.Tracks[j];
                if (track.Times.Length == 0) continue;
                var keyTimes = track.Times.Select(t => (double)t*clip.Duration).ToList();
                bool holdEnd = keyTimes[^1] < clip.Duration;
                if (holdEnd) keyTimes.Add(clip.Duration);
                int times = Floats([.. keyTimes.Select(t => new[] { t })],"SCALAR",minmax: true);
                foreach (var (path,keys,width) in new[] { ("rotation",track.Rotations,4),("translation",track.Positions,3) })
                {
                    if (keys.Length == 0) continue;
                    var values = keys.Chunk(width).Select(k => width == 4 ? Quat(k[0],k[1],k[2],k[3]) : Axes(k[0],k[1],k[2])).ToList();
                    if (holdEnd) values.Add(values[^1]);
                    int output = Floats(values,width == 4 ? "VEC4" : "VEC3");
                    samplers.Add(new Dictionary<string,object?> { ["input"] = times, ["output"] = output, ["interpolation"] = "LINEAR" });
                    channels.Add(new Dictionary<string,object?>
                    {
                        ["sampler"] = samplers.Count-1, ["target"] = new Dictionary<string,object?> { ["node"] = joints[j], ["path"] = path },
                    });
                }
            }
            if (channels.Count == 0 && joints.Count > 0)
            {
                // A static clip still has a duration and timed appearance keys; glTF needs one channel. Its times are
                // [0, duration] as Python ints and floats: min 0, max the duration.
                bool timed = clip.Duration > 0;
                List<double[]> times = timed ? [[0],[clip.Duration]] : [[0]];
                var value = Axes(skel.Joints[0].Offset[0],skel.Joints[0].Offset[1],skel.Joints[0].Offset[2]);
                int input = Floats(times,"SCALAR",minmax: true,min: [0],max: [timed ? (object?)(double)clip.Duration : 0]);
                samplers.Add(new Dictionary<string,object?>
                {
                    ["input"] = input, ["output"] = Floats([.. times.Select(_ => value)],"VEC3"), ["interpolation"] = "STEP",
                });
                channels.Add(new Dictionary<string,object?> { ["sampler"] = 0, ["target"] = new Dictionary<string,object?> { ["node"] = joints[0], ["path"] = "translation" } });
            }
            if (channels.Count > 0) Animations.Add(new() { ["name"] = clipPrefix+clip.Name, ["samplers"] = samplers, ["channels"] = channels });
        }
        return (joints,Skins.Count-1);
    }

    private List<object?> ChildOf(int node)
    {
        if (Nodes[node].GetValueOrDefault("children") is not List<object?> children) Nodes[node]["children"] = children = [];
        return children;
    }

    /// <summary>A node under a joint carrying its bind orientation, where rigid parts on the joint are authored.</summary>
    public int BoneFrame((LskSkeleton Skeleton,List<int> Joints,int Skin) ctx,int joint)
    {
        if (!_frames.TryGetValue((ctx.Skeleton,joint),out int frame))
        {
            var q = ctx.Skeleton.Joints[joint].BindOrientation;
            frame = Math.Abs(q[0]) > 0.99999 ? ctx.Joints[joint]
                : Node(new() { ["name"] = ctx.Skeleton.Joints[joint].Name+" frame", ["rotation"] = Doubles(Quat(q[0],q[1],q[2],q[3])) },ctx.Joints[joint]);
            _frames[(ctx.Skeleton,joint)] = frame;
        }
        return frame;
    }

    // ---- Cloth --------------------------------------------------------------------------------------------------

    /// <summary>lms2gltf.Exporter.cloth: the initial particle grid with each stage's atlas rectangle; physics in extras.</summary>
    public int Cloth(Dictionary<string,object?> entry,int parent)
    {
        var asset = Dict(entry["asset"]);
        var configuration = Dict(asset["configuration"]);
        int width = (int)configuration["width"]!, height = (int)configuration["height"]!;
        var positions = List(asset["particles"]).Select(p => List(Dict(p)["position"])).ToList();
        var uvs = new List<double[]>();
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) uvs.Add([(double)x/(width-1),(double)y/(height-1)]);
        var triangles = new List<int>();
        for (int y = 0; y < height-1; y++)
            for (int x = 0; x < width-1; x++) { int a = y*width+x; triangles.AddRange([a,a+1,a+width,a+1,a+width+1,a+width]); }
        var stages = entry.GetValueOrDefault("uvas") as List<object?> ?? [];
        var diffuse = stages.Count > 0 ? stages[0] as Dictionary<string,object?> : null;
        var mask = stages.Count > 1 ? stages[1] as Dictionary<string,object?> : null;
        string? texture = diffuse != null ? FileOf(diffuse.GetValueOrDefault("texture")) : null;
        string? maskFile = mask != null ? FileOf(mask.GetValueOrDefault("texture")) : null;
        int material = Material(texture,maskFile);
        // A copy: cloth has its own atlas rectangles, and the material may serve ordinary meshes too (shallow, as dict()).
        var copy = new Dictionary<string,object?>(Materials[material]);
        string name = entry.GetValueOrDefault("name") as string is { Length: > 0 } n ? n : "cloth";
        copy["doubleSided"] = true; copy["name"] = name;
        if (maskFile != null) copy["emissiveTexture"] = new Dictionary<string,object?>(Dict(copy["emissiveTexture"])) { ["texCoord"] = 1 };
        Materials.Add(copy);
        var attributes = new Dictionary<string,object?> { ["POSITION"] = Floats([.. positions.Select(Axes)],"VEC3",34962,minmax: true) };
        var stagePair = new[] { diffuse,mask };
        for (int stage = 0; stage < 2; stage++)
        {
            var uva = stagePair[stage];
            List<double[]> mapped = uvs;
            if (uva != null && uva.TryGetValue("asset",out var uvaAsset))
            {
                var rect = List(Dict(uvaAsset)["rect"]);
                double rx = D(rect[0]), ry = D(rect[1]), rw = D(rect[2]), rh = D(rect[3]);
                mapped = [.. uvs.Select(uv => new[] { rx+uv[0]*rw,ry+uv[1]*rh })];
            }
            attributes[$"TEXCOORD_{stage}"] = Floats(mapped,"VEC2",34962);
        }
        int indices = Shorts(triangles,1,"SCALAR",34963);
        Meshes.Add(new()
        {
            ["name"] = name,
            ["primitives"] = new List<object?> { new Dictionary<string,object?> { ["attributes"] = attributes, ["indices"] = indices, ["material"] = Materials.Count-1 } },
        });
        return Node(new()
        {
            ["name"] = name, ["mesh"] = Meshes.Count-1,
            ["extras"] = new Dictionary<string,object?> { ["wotrCloth"] = asset, ["preview"] = "initial particle grid; no wind simulation" },
        },parent);
    }

    // ---- The model walk -----------------------------------------------------------------------------------------

    private static Dictionary<string,object?> OffsetNode(string name,List<object?> offset,double[] relativeScale)
    {
        double w = D(offset[0]), x = D(offset[1]), y = D(offset[2]), z = D(offset[3]), px = D(offset[4]), py = D(offset[5]), pz = D(offset[6]), s = D(offset[7]);
        var node = new Dictionary<string,object?> { ["name"] = name };
        if (w != 1.0 || x != 0.0 || y != 0.0 || z != 0.0) node["rotation"] = Doubles(Quat(w,x,y,z));
        if (px != 0.0 || py != 0.0 || pz != 0.0) node["translation"] = Doubles(Axes(px,py,pz));
        var scale = relativeScale.Select(k => s*k).ToList();
        if (scale.Any(k => Math.Abs(k-1.0) > 1e-6)) node["scale"] = Doubles(scale);
        return node;
    }

    /// <summary>lms2gltf._add_model: one resolved model under node <paramref name="parent"/>. Database scales are absolute,
    /// so an attachment gets the ratio to its parent's.</summary>
    public void AddModel(Dictionary<string,object?> res,int parent,(LskSkeleton Skeleton,List<int> Joints,int Skin)? ctx,int? owner = null,int? attachment = null,
        int depth = 0,bool onJoint = false)
    {
        string name = res.GetValueOrDefault("model") as string is { Length: > 0 } model ? model : Path.GetFileName((string?)res.GetValueOrDefault("file") ?? "?");
        int index = List(Appearance["models"]).Count;
        var appearance = Instance(res,owner,attachment,depth == 0 ? "" : name+"/");
        List(Appearance["models"]).Add(appearance);
        var skeletons = res.GetValueOrDefault("skeletons") as List<object?>;
        var skeletonEntry = skeletons is { Count: > 0 } ? skeletons[0] : null;
        if (skeletonEntry != null && FileOf(skeletonEntry) is { } skeletonFile)
        {
            try
            {
                var skel = LskSkeleton.Parse(resolver.Read(skeletonFile));
                var (joints,skin) = Skeleton(skel,parent,depth == 0 ? "" : name+"/");
                ctx = (skel,joints,skin);
            }
            catch (InvalidDataException error) { Log.Add($"{name}: skeleton skipped ({error.Message}), static export"); ctx = null; }
        }
        var slotFiles = (res.GetValueOrDefault("textures") as List<object?> ?? []).Select(FileOf).ToList();
        var keyed = new Dictionary<int,int>();
        foreach (var state in List(appearance["states"]).Select(Dict))
            foreach (var choice in List(state["choices"]).Select(Dict))
                foreach (var key in List(choice["textureKeys"]).Select(List))
                {
                    var stages = List(key[1]);
                    for (int stage = 0; stage < stages.Count; stage++)
                    {
                        long texture = System.Convert.ToInt64(stages[stage]);
                        if (texture >= 0 && texture < slotFiles.Count && slotFiles[(int)texture] != null) keyed.TryAdd((int)texture,stage);
                    }
                }
        if (keyed.Count > 0)
            appearance["textures"] = Enumerable.Range(0,slotFiles.Count)
                .Select(t => keyed.TryGetValue(t,out int stage) ? (object?)Texture(slotFiles[t]!,keepAlpha: stage == 0).Index : null).ToList();
        var (baseSlot,teamMask) = TextureSlots(DefaultChoice(appearance));
        bool team = (res.GetValueOrDefault("materials") as List<object?> ?? []).Contains(TeamColorMaterial);
        var parts = List(res["parts"]);
        for (int slot = 0; slot < parts.Count; slot++)
        {
            var part = Dict(parts[slot]);
            string? meshFile = FileOf(part["mesh"]);
            if (meshFile == null) { Log.Add($"{name}: missing mesh {(part["mesh"] as Dictionary<string,object?>)?.GetValueOrDefault("file")}"); continue; }
            string? weights = FileOf(part.GetValueOrDefault("weights")), texture = FileOf(part.GetValueOrDefault("texture")), mask = FileOf(part.GetValueOrDefault("mask"));
            if (keyed.ContainsKey((int)baseSlot) && baseSlot >= 0) texture = slotFiles[(int)baseSlot];
            if (team && teamMask >= 0 && keyed.ContainsKey((int)teamMask)) mask = slotFiles[(int)teamMask];
            try
            {
                var m = LmsMesh.Parse(resolver.Read(meshFile));
                if (weights != null && ctx != null && m.Skinned) Mesh(m,texture,weights,ctx.Value.Skin,ctx.Value.Skeleton.Joints.Length,mask: mask);
                else
                {
                    int? anchor = parent;
                    if (!onJoint && ctx != null && m.Attach.Length > 0)
                    {
                        int joint = ctx.Value.Skeleton.JointIndex(m.Attach);
                        if (joint < 0) throw new InvalidDataException($"no joint {m.Attach}");
                        anchor = BoneFrame(ctx.Value,joint);
                    }
                    Mesh(m,texture,parent: anchor,mask: mask);
                }
                List(appearance["parts"])[slot] = Nodes.Count-1;
            }
            catch (InvalidDataException error) { Log.Add($"{name}: part {Path.GetFileName(meshFile)} skipped ({error.Message})"); }
        }
        var attachments = (res.GetValueOrDefault("attachments") as List<object?> ?? []).Select(Dict).Where(a => (string?)a["kind"] == "model").ToList();
        for (int i = 0; i < attachments.Count; i++)
        {
            var a = attachments[i];
            var sub = Dict(a["model"]);
            if (!sub.ContainsKey("parts")) continue;
            int joint = System.Convert.ToInt32(a["joint"]);
            bool on = joint >= 0 && ctx != null && joint < ctx.Value.Joints.Count;
            int anchor = on ? BoneFrame(ctx!.Value,joint) : parent;
            double[] subScale = Scale(sub), ownScale = Scale(res);
            double[] relative = [subScale[0]/ownScale[0],subScale[1]/ownScale[1],subScale[2]/ownScale[2]];
            int node = Node(OffsetNode(sub.GetValueOrDefault("model") as string is { Length: > 0 } subName ? subName : "attachment",List(a["offset"]),relative),anchor);
            AddModel(sub,node,ctx,index,i,depth+1,on);
        }
        var cloths = res.GetValueOrDefault("cloths") as List<object?> ?? [];
        for (int i = 0; i < cloths.Count; i++)
        {
            var a = Dict(cloths[i]);
            var entry = a["cloth"] as Dictionary<string,object?>;
            if (entry == null || entry.GetValueOrDefault("asset") == null) { Log.Add($"{name}: missing cloth"); continue; }
            int joint = System.Convert.ToInt32(a["joint"]);
            bool on = joint >= 0 && ctx != null && joint < ctx.Value.Joints.Count;
            int anchor = on ? BoneFrame(ctx!.Value,joint) : parent;
            int node = Node(OffsetNode(entry.GetValueOrDefault("name") as string is { Length: > 0 } clothName ? clothName : "cloth",List(a["offset"]),[1,1,1]),anchor);
            Dict(List(appearance["cloths"])[i])["node"] = Cloth(entry,node);
        }
    }

    // ---- Result -------------------------------------------------------------------------------------------------

    public GltfModel Result()
    {
        var gltf = new Dictionary<string,object?>
        {
            ["asset"] = new Dictionary<string,object?> { ["version"] = "2.0", ["generator"] = "wotr-reforged lms2gltf" },
            ["scene"] = 0, ["scenes"] = new List<object?> { new Dictionary<string,object?> { ["nodes"] = Scene } },
            ["nodes"] = Nodes.Cast<object?>().ToList(), ["meshes"] = Meshes.Cast<object?>().ToList(), ["materials"] = Materials.Cast<object?>().ToList(),
            ["accessors"] = Accessors.Cast<object?>().ToList(), ["bufferViews"] = Views.Cast<object?>().ToList(),
            ["buffers"] = new List<object?> { new Dictionary<string,object?> { ["byteLength"] = _bin.Count } },
        };
        if (Textures.Count > 0)
        {
            gltf["textures"] = Textures.Cast<object?>().ToList();
            gltf["images"] = Images.Cast<object?>().ToList();
            gltf["samplers"] = new List<object?> { new Dictionary<string,object?> { ["magFilter"] = 9729, ["minFilter"] = 9987, ["wrapS"] = 10497, ["wrapT"] = 10497 } };
        }
        if (Skins.Count > 0) gltf["skins"] = Skins.Cast<object?>().ToList();
        if (Animations.Count > 0) gltf["animations"] = Animations.Cast<object?>().ToList();
        var extras = new Dictionary<string,object?>(Extras);
        if (WriteAppearance) extras["wotrAppearance"] = Appearance;
        if (extras.Count > 0) gltf["extras"] = extras;
        return new()
        {
            Json = gltf, Binary = [.. _bin], ImagePixels = new(_imagePixels), Log = Log,
            Clips = [.. Animations.Select(a => (string)a["name"]!)],
        };
    }
}
