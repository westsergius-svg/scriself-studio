// Authoring tool only. Uses the bundled Presentations runtime, not an application dependency.
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [modules, skillDir, finalPathArg] = process.argv.slice(2);
if (!modules || !skillDir || !finalPathArg) throw new Error('Usage: node tools/build-training-presentation.mjs <bundled node_modules> <presentations skill> <new output.pptx>');
process.env.RUNTIME_NODE_MODULES = path.resolve(modules);
const requireRuntime = createRequire(path.join(path.resolve(modules), '..', 'presentation-author.cjs'));
const { Presentation, PresentationFile } = await import(pathToFileURL(requireRuntime.resolve('@oai/artifact-tool')).href);
const sharp = requireRuntime('sharp');
const { finalizePresentation } = await import(pathToFileURL(path.join(skillDir, 'container_tools/artifact_tool_utils.mjs')).href);
const version = (await fs.readFile(path.join(repo, 'Directory.Build.props'), 'utf8')).match(/<Version>([^<]+)<\/Version>/)[1];
const trainingRoot = path.join(repo, 'src/ScriSelfStudio.Presentation/Help/Training');
const slides = JSON.parse(await fs.readFile(path.join(trainingRoot, 'training.json'), 'utf8'));
const sources = JSON.parse(await fs.readFile(path.join(repo, 'tools/training/sources.json'), 'utf8'));
const buildDir = path.join(repo, 'artifacts/training-presentation-20261104/build');
const finalPath = path.resolve(finalPathArg);
const screenDir = path.join(repo, 'tools/training/screens');
await fs.mkdir(screenDir, { recursive: true });
await fs.mkdir(path.join(trainingRoot, 'Slides'), { recursive: true });
await fs.mkdir(buildDir, { recursive: true });
await fs.mkdir(path.dirname(finalPath), { recursive: true });

const presentation = Presentation.create({ slideSize: { width: 1600, height: 900 } });
function addText(slide, name, text, x, y, width, height, size, color = '#F0F0F0', bold = false) {
  const shape = slide.shapes.add({ name, geometry: 'textbox', position: { left:x, top:y, width, height }, fill:'none', line:{fill:'none',width:0} });
  shape.text = text;
  shape.text.style = { typeface:'Segoe UI', fontSize:size, color, bold, verticalAlignment:'top' };
  return shape;
}

for (const lesson of slides) {
  const source = sources.find(s => s.Id === lesson.Id);
  const screenPath = path.join(screenDir, lesson.Id+'.png');
  // Screens are real app captures. Cropping only enlarges the controls discussed in each lesson.
  try { await fs.access(screenPath); }
  catch {
    let screen = sharp(path.join(repo, source.Source));
    if (source.Crop) screen = screen.extract(source.Crop);
    await screen.png().toFile(screenPath);
  }
  const slide = presentation.slides.add();
  slide.background.fill = '#242424';
  addText(slide,'brand',`ScriSelf Studio ${version}`,58,36,1000,34,23,'#BEBEBE');
  addText(slide,'title',lesson.Title,58,125,406,165,48,'#FFFFFF',true);
  addText(slide,'instructions',lesson.Body,58,310,410,300,27,'#E4E4E4');
  addText(slide,'practice-title','Попробуйте сами',58,651,410,36,21,'#78B6F5',true);
  addText(slide,'practice',lesson.Practice,58,700,410,132,25,'#E4E4E4');
  const screenshot = await fs.readFile(screenPath);
  slide.images.add({blob:screenshot,contentType:'image/png',alt:lesson.Screen,fit:'contain',position:{left:510,top:116,width:1032,height:674}});
  addText(slide,'number',`${lesson.Number} / ${slides.length}`,1455,838,95,30,20,'#BEBEBE');
  slide.speakerNotes.text = `${lesson.Transcript || lesson.Body}\n\nЗадание: ${lesson.Practice}\nИсточник иллюстрации: собственный снимок интерфейса ScriSelf Studio, ${source.Source}. Использовано кадрирование без перерисовки элементов.`;
  const png = await presentation.export({slide,format:'png',scale:1});
  await fs.writeFile(path.join(trainingRoot,'Slides',lesson.AssetFile),new Uint8Array(await png.arrayBuffer()));
  console.log(`Rendered ${lesson.Number}: ${lesson.Title}`);
}

const candidatePath = path.join(buildDir,'candidate.pptx');
await (await PresentationFile.exportPptx(presentation)).save(candidatePath);
const result = await finalizePresentation({
  workspaceDir:repo,candidatePath,finalPath,
  pythonExecutable:process.env.TRAINING_PYTHON || 'python',
  integrityValidatorPath:path.join(skillDir,'container_tools/inspect_presentation_package_integrity.py'),
  layoutValidatorPath:path.join(skillDir,'container_tools/inspect_presentation_layout_geometry.py'),
  layoutArgs:['--expected-slide-size-emu','15240000,8572500','--validate-heading-fit'],
  fontPolicy:{basis:'design',families:['Segoe UI']},
  verifyArtifactToolImport:true,
  receiptPath:path.join(buildDir,path.basename(finalPath)+'.validation.json'),
});
console.log(JSON.stringify(result));
