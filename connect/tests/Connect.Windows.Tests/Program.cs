using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using RS2XboxConnect;

var root = Path.Combine(Path.GetTempPath(), "RS2ConnectDpapi-" + Guid.NewGuid().ToString("N"));
PrivateSettings.RestrictDirectory(root);
var path = Path.Combine(root, "settings.protected");
var settings = new Settings { Username = "testuser", Password = "dpapi-test-password" };
settings.DirectIdentities["test-world"] = DirectIdentity.Create();
settings.XboxCharacters["test-xbox"] = new XboxCharacter("tester","private-character");
PrivateSettings.Save(path, settings);
if (Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(settings.Password)) throw new Exception("Plaintext secret found");
var loaded = PrivateSettings.Load(path);
if (loaded.Password != settings.Password || loaded.Username != settings.Username) throw new Exception("DPAPI roundtrip failed");
if (loaded.DirectIdentities["test-world"] != settings.DirectIdentities["test-world"] || loaded.XboxCharacters["test-xbox"] != settings.XboxCharacters["test-xbox"]) throw new Exception("Direct identity/character roundtrip failed");
var ciphertext = Encoding.UTF8.GetString(File.ReadAllBytes(path));
if (ciphertext.Contains(settings.DirectIdentities["test-world"].Secret) || ciphertext.Contains("private-character")) throw new Exception("Direct credentials were stored in plaintext");
var acl = new DirectoryInfo(root).GetAccessControl();
if (!acl.AreAccessRulesProtected) throw new Exception("Directory inherited broad ACLs");
var permitted = new[] { WindowsIdentity.GetCurrent().User!.Value, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value };
foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
    if (rule.AccessControlType == AccessControlType.Allow && !permitted.Contains(rule.IdentityReference.Value)) throw new Exception("Unexpected directory access");
Console.WriteLine("PASS: Windows DPAPI credential roundtrip, ciphertext excludes plaintext password, and private user/SYSTEM directory ACL");
File.WriteAllBytes(path, new byte[32]);
try { PrivateSettings.Load(path); throw new Exception("Accepted invalid DPAPI data"); }
catch (System.ComponentModel.Win32Exception) { Console.WriteLine("PASS: corrupted settings rejected"); }
