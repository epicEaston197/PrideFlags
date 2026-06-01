#!/usr/bin/env dotnet
#:package NetVips@3.2.0
#:package System.CommandLine@2.0.0

// This script requires libvips (tested with API 8.0) and resvg (as a binary in the path)

using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.CommandLine;
using System.CommandLine.Parsing;
using NetVips;

const double SVG_RENDER_SIZE = 4096;
const string SVG_RENDER_DIR = "PNGs/.render";

var inOption = new Option<string>("--in")
{
    Description = "The SVG file or a directory of SVGs to render",
    DefaultValueFactory = _ => "SVGs"
};

var rootCommand = new RootCommand("A utility for rendering SVGs for the Yellow-Dog-Man/PrideFlags repository.")
{
    Options = { inOption }
};


// Render the SVG to PNG at a high resolution without antialiasing using resvg
// we render without antialiasing and at a high resolution so that the image can procesed without information loss from antialiasing
// we use resvg instead of libvips for rendering the svg because libvips uses librsvg
// librsvg does not render the svgs we have correctly while resvg does
async Task<string> RenderSVGFromPathToPNG(string inputPath)
{
    var renderedFileName = Path.ChangeExtension(Path.GetFileName(inputPath), ".png");
    Console.WriteLine($"Rendering: {inputPath}");
    var renderedPath = Path.Join(SVG_RENDER_DIR, renderedFileName);
    var process = Process.Start(
        "resvg",
        [
            inputPath,
            renderedPath,
            "--width", $"{SVG_RENDER_SIZE}",
            "--height", $"{SVG_RENDER_SIZE}",
            "--shape-rendering", "crispEdges"
        ]
    );
    await process.WaitForExitAsync().ConfigureAwait(false);
    Console.WriteLine($"Finished: {inputPath}");
    return renderedPath;
}

Image ProcessImageFromPath(string input, double scale)
{
    using var image = Image.NewFromFile(input, access: Enums.Access.Random);
    return ProcessImage(image, scale);
}

// we process the image here
// additional steps can be added if needed
Image ProcessImage(Image input, double scale)
{
    return input.Resize(scale);
}

void RenderSVGFromPath(string inputPath, double size)
{
    Console.WriteLine($"Rendering: {inputPath}");

    if (size > SVG_RENDER_SIZE)
    {
        throw new Exception($"Processed PNG size '{size}' can't be larger than 'SVG_RENDER_SIZE' of '{SVG_RENDER_SIZE}'");
    }

    var scale = size / SVG_RENDER_SIZE;

    // handle the `Color_` part of the name
    if (Path.GetFileNameWithoutExtension(inputPath).Split(['_'], 2) is [var kind, var name])
    {
        var outName = $"{kind}_{size}_{name}";
        using var processedImage = ProcessImageFromPath(inputPath, scale);
        processedImage.WriteToFile($"PNGs/{kind}_{size}/{outName}.png");
        Console.WriteLine($"Finished: {inputPath}");
    }
    else
    {
        throw new Exception($"Expected at least one '_' in the SVG name for '{inputPath}'");
    }
}


rootCommand.SetAction(async parseResult =>
{
    var svgPath = parseResult.GetValue(inOption)!;

    // processing 4K images can be a heavy operation
    // a limit of 6 is what worked best for me on my machine
    // - bree
    var parallelOptions = new ParallelOptions()
    {
        MaxDegreeOfParallelism = 6
    };

    var isPathDirectory = File.GetAttributes(svgPath).HasFlag(FileAttributes.Directory);

    if (isPathDirectory)
    {
        await Parallel.ForEachAsync(Directory.EnumerateFiles(svgPath), parallelOptions, async (inputPath, token) =>
        {
            var renderedSVGPath = await RenderSVGFromPathToPNG(inputPath);
            RenderSVGFromPath(renderedSVGPath, 1024);
            RenderSVGFromPath(renderedSVGPath, 128);
        });
    }
    else
    {
        var renderedSVGPath = await RenderSVGFromPathToPNG(svgPath);
        RenderSVGFromPath(renderedSVGPath, 1024);
        RenderSVGFromPath(renderedSVGPath, 128);
    }
});

rootCommand.Parse(args).Invoke();