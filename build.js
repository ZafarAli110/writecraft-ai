#!/usr/bin/env bun
import { execSync } from 'child_process';
import { existsSync, rmSync, mkdirSync, statSync, readdirSync, cpSync, copyFileSync, writeFileSync } from 'fs';
import { resolve, join } from 'path';
import { homedir } from 'os';

const clean = process.argv.includes('--clean');

console.log('🔨 Building WriteCraftAI...\n');

if (clean) {
    console.log('🧹 Cleaning ALL artifacts...');
    ['bin', 'obj', 'publish', 'release'].forEach(dir => {
        if (existsSync(dir)) rmSync(dir, { recursive: true, force: true });
    });
}

console.log('📦 Restoring packages...');
execSync('dotnet restore', { stdio: 'inherit' });

console.log('\n📦 Publishing...');
execSync('dotnet publish WriteCraftAI.csproj --configuration Release --runtime win-x64 --self-contained true', { 
    stdio: 'inherit' 
});

const publishPath = resolve('bin/Release/net8.0/win-x64/publish');
const distPath = resolve('release/WriteCraftAI');

// ✅ Create clean distribution structure
console.log('\n📁 Creating clean distribution structure...');
rmSync(distPath, { recursive: true, force: true });
mkdirSync(distPath, { recursive: true });
mkdirSync(join(distPath, 'AIModels'), { recursive: true });

// ✅ Copy all publish output to dist
console.log('📦 Copying application files...');
cpSync(publishPath, distPath, { recursive: true });

// ✅ Copy AVX-optimized native libraries from NuGet cache
console.log('🔧 Copying AVX-optimized native libraries...');
const nugetCache = join(homedir(), '.nuget/packages/llamasharp.backend.cpu/0.25.0/runtimes/win-x64/native');
const targetRuntimes = join(distPath, 'runtimes/win-x64/native');

// ✅ Copy External tools (ffmpeg)
console.log('📦 Copying external tools...');
if (existsSync('External')) {
    mkdirSync(join(distPath, 'External'), { recursive: true });
    cpSync('External', join(distPath, 'External'), { recursive: true });
    console.log('✅ Copied External tools');
}

if (existsSync(nugetCache)) {
    // Ensure target exists
    mkdirSync(targetRuntimes, { recursive: true });
    cpSync(nugetCache, targetRuntimes, { recursive: true });
    const subdirs = readdirSync(targetRuntimes);
    console.log('✅ Copied AVX variants:', subdirs.join(', '));
} else {
    console.error('❌ NuGet cache not found:', nugetCache);
    process.exit(1);
}

// ✅ Create user guide
const userGuide = `WriteCraftAI - Local AI Writing Assistant
==========================================

QUICK START:
1. Download an AI model (Qwen2.5-7B recommended):
   https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF

2. Place the .gguf file in the "AIModels" folder

3. Double-click WriteCraftAI.exe to start

4. Your browser will open automatically to http://localhost:5555

CONFIGURATION:
- Edit config.json to change settings
- Model path, thread count, and other options can be adjusted

REQUIREMENTS:
- Windows 10/11 (64-bit)
- 8GB+ RAM recommended for 7B models
- 16GB+ RAM recommended for 14B models

SUPPORT:
- All processing happens locally on your computer
- No internet connection required after setup
- Your data never leaves your machine
`;

writeFileSync(join(distPath, 'README.txt'), userGuide);
console.log('✅ Created README.txt');

// ✅ Create AIModels placeholder
writeFileSync(join(distPath, 'AIModels', 'PUT_YOUR_MODEL_HERE.txt'), 
    'Download a .gguf model file and place it in this folder.\n\nRecommended: Qwen2.5-7B-Instruct-Q4_K_M.gguf');

// ✅ Remove unnecessary files from distribution
console.log('🧹 Cleaning up unnecessary files...');
const filesToRemove = [
    'appsettings.Development.json',  // Not needed in production
    'WriteCraftAI.pdb',              // Debug symbols
];
filesToRemove.forEach(file => {
    const path = join(distPath, file);
    if (existsSync(path)) rmSync(path);
});

// ✅ Create ZIP
console.log('\n📦 Creating ZIP...');
execSync(`powershell "Compress-Archive -Path '${distPath}\\*' -DestinationPath 'release/WriteCraftAI-Windows-x64.zip' -Force"`);

const size = (statSync('release/WriteCraftAI-Windows-x64.zip').size / 1024 / 1024).toFixed(1);
console.log(`\n✅ ZIP created: ${size} MB`);

// ✅ Show final structure
console.log('\n📂 Distribution structure:');
console.log('   WriteCraftAI/');
console.log('   ├── WriteCraftAI.exe      (main application)');
console.log('   ├── config.json           (user settings)');
console.log('   ├── urdu-vocabulary.json  (vocabulary data)');
console.log('   ├── README.txt            (user guide)');
console.log('   ├── AIModels/             (put your .gguf model here)');
console.log('   ├── wwwroot/              (web assets)');
console.log('   └── runtimes/             (LlamaSharp native libraries)');
console.log('\n🎉 Done! Users just need to add a model and run WriteCraftAI.exe');