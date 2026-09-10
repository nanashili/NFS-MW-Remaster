import Foundation
import AVFoundation
import AppKit
let asset = AVURLAsset(url: URL(fileURLWithPath: CommandLine.arguments[1]))
let duration = CMTimeGetSeconds(asset.duration)
print("duration=\(duration) video=\(asset.tracks(withMediaType: .video).first?.naturalSize ?? .zero) audioTracks=\(asset.tracks(withMediaType: .audio).count)")
let generator = AVAssetImageGenerator(asset: asset)
generator.appliesPreferredTrackTransform = true
generator.maximumSize = CGSize(width: 960, height: 600)
generator.requestedTimeToleranceBefore = .zero
generator.requestedTimeToleranceAfter = .zero
let output = CommandLine.arguments[2]
try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
let times = CommandLine.arguments.count > 3 ? CommandLine.arguments[3].split(separator: ",").compactMap { Double($0) } : stride(from: 0.0, to: duration, by: max(1, duration / 16)).map { $0 }
for seconds in times {
    let frame = try generator.copyCGImage(at: CMTime(seconds: seconds, preferredTimescale: 600), actualTime: nil)
    let bitmap = NSBitmapImageRep(cgImage: frame)
    let path = String(format: "%@/frame-%07.3f.jpg", output, seconds)
    try bitmap.representation(using: .jpeg, properties: [.compressionFactor: 0.9])!.write(to: URL(fileURLWithPath: path))
    print(path)
}
