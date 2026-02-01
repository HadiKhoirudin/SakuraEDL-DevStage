// ============================================================================
// LoveAlways - MediaTek SLA Key Database
// MediaTek SLA Key Database
// ============================================================================
// Reference: mtkclient project sla_keys.py
// Note: Contains only verified real keys
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System.Collections.Generic;
using System.Numerics;

namespace LoveAlways.MediaTek.Auth
{
    /// <summary>
    /// SLA Key Record
    /// </summary>
    public class SlaKeyRecord
    {
        public string Vendor { get; set; }
        public string Name { get; set; }
        public ushort[] DaCodes { get; set; }
        public string D { get; set; }  // Private key exponent (Hex)
        public string N { get; set; }  // Modulus (Hex)
        public string E { get; set; }  // Public key exponent (Hex)
    }

    /// <summary>
    /// MediaTek SLA Key Database
    /// </summary>
    public static class MtkSlaKeys
    {
        private static readonly List<SlaKeyRecord> _keys = new List<SlaKeyRecord>();

        static MtkSlaKeys()
        {
            InitializeKeys();
        }

        private static void InitializeKeys()
        {
            // Tecno/Infinix (Verified real keys)
            _keys.Add(new SlaKeyRecord
            {
                Vendor = "Tecno/Infinix",
                Name = "",
                DaCodes = new ushort[] { 0x1208, 0x0907 },
                D = "499890AEF768030B56AD8BF7355262A57643A8C3DF318647B72E0B72416D91D25882D0077FEBE9E8275E9F42C3F89FFF5E99E0163487461B7CC3E97C1CC4F4E2D587AB7D8DB80335AA8E3254F318AAC42136FF1043649918685A0F5CF5548318090C26D951A2B93F79FB0D9B5E1DB1C928091E1908A5DA7D3B9194A22B18B78C6EF88932EFB3D88DB8BA7117257063DE165FA8534D50C6B35F6F7C0AF5F4A96BB89756BB7AC94110E8D1B5868A5DAADF815FE8FA38960A039D681F319C7B7ED7A55649C2F2F75B26C27807AFED8BC4EF57619FF9DA152E337FE379E1B1B0020C1EF2ABDCE4A66AAF3802EE8AB105ACA6F3388EA4C20184E572778C8EB3D30B51",
                N = "BA4C5178EE5477B7A30FC99F9FE78E7C011E58B5DC05832591AADEE0D26A2667738FFE851782016CAE7DB8DC1958C627BC60E3A96D46E225536DCC76CBFADFE6CA2CBE4BE982AB46CBB0F1C5CB2CB13D7BBAC96E467D76AF819DCA28FB90A03AF92DDC05B34506E24F02A049D73B8143A1CF4B081372D49F4BCA21DA9EE8A19679669C71C836FC5B8180BD98256BA2AE82B702DDC61EDFD9C47871D027297D6C72A984046081953D141656F0DC4BC4E2D4006147BE096D7E855AA7057460EAC7C57660F1F28BE278C38700BAC5EBADB39F6508CF2F1E4044421664A856623FFB2002C1204CED786D576E3DDAA7C185FD698B1185A98A03B875F0A93831270557",
                E = "010001"
            });

            // Generic - CodeSigKey (Verified)
            _keys.Add(new SlaKeyRecord
            {
                Vendor = "Generic",
                Name = "CodeSigKey",
                DaCodes = new ushort[] { },
                D = "00D57BCF5934CE1A035F4598B42DAD4BC8AE7577FB6D81FA232317E8EE7C4FBB33772EFE378DF7DFE5369BB9ACAB1008FA1CFBA737890012A883B372B15932335B689B46A32F1B383B75F0DE2E1B5B0B9F4E1C3E780C2AB0CD3671EB4E34F30BB4C630A60D168CA124810D0F91A1ACC8EFDCEE52D4762BB35813BCC93878E1D15B750561B78006B4C13A8F76B5F10C941E5776C21192357A9B9D7E02C0FC812D2B154671863DE97CE3ED07F90624A0CADD04079E145168A3558A64786820192F5B638354DA69520288B976296961C337FB18A90120F2B6B365C0E1A57CE4119AE8BC718E08FDA33F1F42AA1C91AED090EC6B5656A66C246F89FDE5FD41A76671",
                N = "01C6B6A1DDF05E818E3DFE16101C5DF65939F352EAB8AACA91CB5BFEC15A1989DD7553343683BC30BB38E45F15BF17BCCAB16A41D695A4318F26504675FE83E92EE21C991C0FBA705395B4A34C331842D8A6F69846B58CC67306E3DE27B05666A6C4372E3FC0D92F314805EDD5B1CB7D25BF3CF9CA9C33C36D97B0B37DA8A44A7A1CA651679D8D680557740C7C1CA25D84BDD12136C2930432808F28265D1E33E667389E4806D865F3CC06329534F7A11861EB688545DCCCEF0B04E96735A08368FA31A1F3260073B31299B216192E620B8D1EA468925ADBD627C49EFC3623658F3CF8AD6D8556272E48FA7711E650287DA19196610F036B6C0D394E42C121D1",
                E = "010001"
            });

            // Generic - SecureROSigKey (Verified)
            _keys.Add(new SlaKeyRecord
            {
                Vendor = "Generic",
                Name = "SecureROSigKey",
                DaCodes = new ushort[] { },
                D = "040AB412E994921780E7D3AC4E665B5018BAD2221D93A236FCD3D4245BB14EF5E715B687254BFC5D5C058FF5C33AF644E6B03748A6ABFDFAFF808265B9C12B42C2164826B3A8CD5D6B3295E025618AED68D33E02D75FB8C69FDE6753AF454EAA92F448961C5D11DFD8D2D5125E54C71DE5792EAD4B4AD2A47ED2F144C664A2EC2B5C527D4C4570162EBADF6FA6AA19D86C927257BDA4AC4B471AD94AC16C8A97E9201101EF268E35E66835FE9831F3D18495BA15B3FCD9089B4569F770896674173647EFED86F4570CE6118E48A7EDD6CE2A2ED72A2E6FD615A323708F0881443B6C6DEF3800B392385E060AAC6CE086DDBA9227027F80B0DFDF691A1ED8A601",
                N = "041EDF18B2702830A0D1D6D2C0C9833C46D55636929ABC8CBF62D03A8E352697F536DDF250E6CA5F1153A9227FEA4F6C4A91CF118FD821ABA9020FFC6AA05D4C361177AA384ACEE201E1326D5A0E1D566E74DA51CD735841FB5638E03F4E9A5AC58D0123F8E057686541E424F83508D1CEADDE7A893F57B852ECFF27953F53030953859EA265C0C7155F6B730E902BC23FEB58077E8D439606B164635D5AA0C53657BF2143EEE86F06781573BED22DD3E792591A263F2357CC42AEF4B8DB585987A311A022C4442A4DFA1C4891D8ADA1B231A92096E16D9C718FB09DFC0A5B008BB8243BF32C537A6B19542E37311085197B6BE8DA54EE1D6BC28BA94E0079CB",
                E = "010001"
            });

            // IOT (Verified)
            _keys.Add(new SlaKeyRecord
            {
                Vendor = "IOT",
                Name = "",
                DaCodes = new ushort[] { },
                D = "be530d5c62547054b7a92baf554d319aaebb34ed3a6409c94820d301f4440a5436744071542f72bca054e941fe200ec1f2a07b249678363eec92d29ca7af14c5c398cc17c340ee2e3a12d7b5826b0add6a5656d27863d107a867e198b615e4597e736eaa69281a36ba4390f437ba43d0bd02e235b5db58cba2d8fffdc119b4c5847e33c0fd6e4559fa2b73b895df4382ec532741d4610a248c2762fd0c30447f91bf9667f8e88529f2df2e2aa39fce1cae9fb37d7eb2cfbd17db932efee26bf7b39593e2b82a5c3c7f39e0622b4c0bfa4d54cf49a7d1e8a1ebd96bdbbe61088834716bba50c6dce9e745b81c6ba45643c3a61484cc9c8c9588ee1339c7ab6d81",
                N = "E8E6DFE50036D970ACD615E03A1BD5701A69FB42A16B2679E1921CBB5603173B72C7D6BA234CAF94B6565AB36BB6B772D2C11099F69DA635211AF984092844EEA8E6967A7B0F8D32CDB32F04A807DD51752AD0CBD1394F58A0FD5A13F5BEB276FAA69972BD4373F550694A82A1DD37DD481E96AB78540BBC240B68C0E3B7FBB888434F7FF6F9C17FA53569C3C01CDFFD6BCB68A343C79733BC7057571836941E9E7CF2421C04806676B43491A6B3DAB9C09067683D3C37B1ECDD3CA37F4BB422893D38C63C30021D2D1E1E97F19473F0C84D7EF73EEFE7A99411F8023B1FF411FC1F9568340E688239C2CE973EF1C48459B971AE579E4B5E7E8C68CD92AF6C9F",
                E = "010001"
            });

            // === Real keys from mtkclient local project ===
            
            // Motorola G24 (2024 model, tested to support Carbonara)
            _keys.Add(new SlaKeyRecord
            {
                Vendor = "Motorola",
                Name = "Moto G24",
                DaCodes = new ushort[] { },
                D = "00a89df958cec69e5e82f12cc64f21b577a99916043912cc47ed278f88cb79ba847e7601abd8c502beef0bc706038a9c5269486c191b65da800dcd465028ccd5e530beb93e02053ac49d1ff4f17be3245b0bbd0ca7ea51558c439783648502e9ff92ac3696cadf09603d1f89c1d1d09095ee5ee68cace1b3a401ef401de86d3911ea96021dc5b5af36e6babf3d48d6a58a9075d5deeacadbfd09f93748929ef466a9d339d92370334e0e50afc0c43cfb1b9f2bff5e3b5a7012b93e92d8644f032993033245eec56d899837d1080c5aba7e09fe2e2eac06921159775392d64e819daa905a2931352ae02e3a21318b207e0fbfa113e8a32a37987243da2bb57d7d89",
                N = "00db8f46cf8da80af8cca1aec9ff7b358cfe4cc5659ade5ef9c196905caaf979658349284723bef9524532b21f460c0897468be95d0aa92682144d1bfcb84afc7712ff3b5dc34153e5efe64b465a6d8cf2bd8c2fb1bf27d9c77f26e90baa3ddada18525d3f689441ef7b6dc5c4b8c496b0a9c92f29d26dac8ff8b137d6a93cf26ad391bf6124207ff9eb26e10b65269c6bad38eff0c50aab604a0128b874f24263037c605bc9f855252f78173141d166b632dbb549370af71efdc522532cb55c48b9a39a21ee8e0cc8bb34c394aec92155a16f95b646aa9e5f88c989eaf2d7f615bf5afe619e27dfab5adbbd7999db9590ab0f30c95c98da39616cad6494be52b7",
                E = "010001"
            });
            
            // KaiOS
            _keys.Add(new SlaKeyRecord
            {
                Vendor = "KaiOS",
                Name = "lk/files/pbp/keys/toolauth/da_prvk.pem",
                DaCodes = new ushort[] { },
                D = "928d2a63d56bc42c3c02e836c025f6db39f06d57480dc705f3eaf238120a2b0d8fc00ec3cdf209615ca41bf73ae499dd9accb09b99fbb4046087008aab48d96f437292c160092dfc4ae33f94acee374e584b9d70d90ca46664d31cf72bba302e6eafab68e13a7f1ed9fa0ddc2604f3164bc97bc8c1b0be9db1d76d1d970ea36f4af8ad2ce0ab5b7ca6b4f99c133fa3f8ff9c92da874394d0e8f1bdbb83c8e811a344a5b5a7251c9b4fb4ad0ac494a2c50a1a79fa9b3992d749535b691dc2f016cee493e41cd2033190c4c0497f689e48bae5d3ad28cb0e8d17dae7f4e8c034ab60ab330f678fbfdd2c9716bfa6a617339d248b46df1593d5317b3af44a864641",
                N = "9BB734774443D77557A76E24B10733787750D90D09C869CD606D54F28978EA6220DC9948B3C9E89284F8551D6166F3754B6A3B890AC9CDA9E37DFAA0C1317E351CE5107C4273795949C6CCE638314AB1A345385D7642CB8D055A1F410C7D7E24A6F0A2AAB8184E773D21B3754A947541680F2C1A8D6BA5BEFD3B6E1FC28EC0B61D55B1454383F2C3E8BD27170A25978608F6788B90A2FC34F0CE35056BF7520795C8C60232CBBC0B0399367AF937869CA45CF737A8A066127893E93166C433298DD6FD009E6790E743B3392ACA8EA99F61DFC77BD99416DDA4B8A9D7E4DA24217427F3584119A4932016F1735CC63B12650FDDDA73C8FCFBC79E058F36219D3D",
                E = "010001"
            });

            // TODO: Add more verified manufacturer keys
            // Key sources:
            // 1. mtkclient project sla_keys.py (Verified)
            // 2. Extracted from real device certificates
            // 3. Official authentication files
            //
            // Note: Do not add forged or unverified keys
        }

        /// <summary>
        /// Get SLA key by DA Code
        /// </summary>
        public static SlaKeyRecord GetKeyByDaCode(ushort daCode)
        {
            foreach (var key in _keys)
            {
                if (key.DaCodes != null)
                {
                    foreach (var code in key.DaCodes)
                    {
                        if (code == daCode)
                            return key;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Get SLA key by vendor
        /// </summary>
        public static SlaKeyRecord GetKeyByVendor(string vendor)
        {
            foreach (var key in _keys)
            {
                if (key.Vendor.ToLower().Contains(vendor.ToLower()))
                    return key;
            }
            return null;
        }

        /// <summary>
        /// Get generic keys (for trial)
        /// </summary>
        public static IEnumerable<SlaKeyRecord> GetGenericKeys()
        {
            foreach (var key in _keys)
            {
                if (key.Vendor == "Generic" || key.DaCodes == null || key.DaCodes.Length == 0)
                    yield return key;
            }
        }

        /// <summary>
        /// Get all keys
        /// </summary>
        public static IReadOnlyList<SlaKeyRecord> GetAllKeys()
        {
            return _keys.AsReadOnly();
        }
    }
}
