using DLP_NIR_Win_SDK_CS;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace DLP_NIR_Win_SDK_WinForm_App_CS
{
    public partial class ActiveKeyManageForm : Form
    {
        public class ListViewData
        {
            public ListViewData()
            {
                // default constructor
            }

            public ListViewData(String sernum, String key)
            {
                SerNum = sernum;
                Key = key;
            }

            public String SerNum { get; set; }
            public String Key { get; set; }
        }
        public ActiveKeyManageForm()
        {
            InitializeComponent();
            toolStripStatusLabel1.Text = "";
            listView1.View = View.Details;
            listView1.FullRowSelect = true;

            //Add column header
            listView1.Columns.Add("S/N", 100);
            listView1.Columns.Add("Key Pairs", 280);
            listView1.Items.Clear();

            foreach (ListViewData row in ReadFromFile())
            {
                string[] arr = new string[2];
                ListViewItem itm;
                arr[0] = row.SerNum;
                arr[1] = row.Key;
                itm = new ListViewItem(arr);
                listView1.Items.Add(itm);  // Add data to list view
            }
                
            textBox_sn.Focus();
        }

        private void button_getsn_Click(object sender, EventArgs e)
        {
            textBox_sn.Text = Device.DevInfo.SerialNumber;
            textBox_sn.Focus();
        }
        //用來比較textkey是否有處理
        String tmpkey = "";
        private void textBox_key_TextChanged(object sender, EventArgs e)
        {
            if (tmpkey != textBox_key.Text)
            {
                Regex rgx = new Regex("[^a-fA-F0-9]");
                String key = textBox_key.Text;
                key = rgx.Replace(key, "");
                //暫存處理key
                String buf = "";
                for (int i = 0; i < key.Length; i++)
                {
                    buf += key.Substring(i, 1);
                    if (i % 2 == 1 && i != key.Length - 1)
                    {
                        buf += " ";
                    }
                }
                tmpkey = buf;
                textBox_key.Text = buf;
                //將游標移到最後
                textBox_key.Select(textBox_key.Text.Length, 0);
            }
        }

        private void button_add_Click(object sender, EventArgs e)
        {
            String[] key = textBox_key.Text.Split(new char[] { ' ', ':', ';', '-', '_' });
            List<ListViewData> ItemsList = new List<ListViewData>();
            Boolean isNewData = true;

            if (textBox_key.Text.Length != 35 || key.Length != 12)
            {
                Message.ShowError("The key format is incorrect, please check if the string outside the space is 2 characters 1 group, and total 12 groups.");
                return;
            }

            // Read data from list view
            for(int i=0;i<listView1.Items.Count;i++)
            {
                String sn = listView1.Items[i].SubItems[0].Text;
                String snkey = listView1.Items[i].SubItems[1].Text;
                ListViewData data = new ListViewData();
                data.SerNum = sn;
                data.Key = snkey;
                ItemsList.Add(data);
            }

            for (int index = 0; index < ItemsList.Count; index++)
            {
                if (ItemsList[index].SerNum == textBox_sn.Text)
                {
                    String text = "Do you want to replace " + textBox_sn.Text + "'s key?";
                    DialogResult result = Message.ShowQuestion(text,null, MessageBoxButtons.OKCancel);
                    string[] arr = new string[2];
                    ListViewItem itm;
                    if (result == DialogResult.OK)
                    {
                        listView1.Items.RemoveAt(index);
                        arr[0] = textBox_sn.Text;
                        arr[1] = textBox_key.Text;
                        itm = new ListViewItem(arr);
                        listView1.Items.Insert(index, itm);
                        initListviewSelect();
                        listView1.Items[index].Selected = true;
                        listView1.Focus();
                        isNewData = false;
                        break;
                    }
                    else
                        return;
                }
            }

            if (isNewData)
            {
                string[] arr = new string[2];
                ListViewItem itm;
                arr[0] = textBox_sn.Text;
                arr[1] = textBox_key.Text;
                itm = new ListViewItem(arr);
                listView1.Items.Add(itm);
                initListviewSelect();
                listView1.Items[listView1.Items.Count - 1].Selected = true;
                listView1.Focus();
            }

            ItemsList.Clear();
            // Read data from list view
            for (int i = 0; i < listView1.Items.Count; i++)
            {
                String sn = listView1.Items[i].SubItems[0].Text;
                String snkey = listView1.Items[i].SubItems[1].Text;
                ListViewData data = new ListViewData();
                data.SerNum = sn;
                data.Key = snkey;
                ItemsList.Add(data);
            }
            SaveToFile(ItemsList);

            textBox_sn.Text = "";
            textBox_key.Text = "";
        }
        private void SaveToFile(IEnumerable<object> rows)
        {
            String FileName = Path.Combine(MainWindow.ConfigDir, "ActictionKey.xml");
            DBG.WriteLine("Save key pairs to {0}", FileName);

            // Save data to file
            XmlSerializer xml = new XmlSerializer(typeof(List<ListViewData>));
            TextWriter writer = new StreamWriter(FileName);
            xml.Serialize(writer, rows);
            writer.Close();
        }
        private IEnumerable<object> ReadFromFile()
        {
            String FileName = Path.Combine(MainWindow.ConfigDir, "ActictionKey.xml");
            DBG.WriteLine("Read key pairs from {0}", FileName);
            List<ListViewData> rows = new List<ListViewData>();

            if (File.Exists(FileName))
            {
                XmlSerializer xml = new XmlSerializer(typeof(List<ListViewData>));
                TextReader reader = new StreamReader(FileName);
                rows = (List<ListViewData>)xml.Deserialize(reader);
                reader.Close();
            }

            return rows;
        }

        private void button_del_Click(object sender, EventArgs e)
        {
            int index = listView1.FocusedItem.Index;
            if (index < 0)
            {
                Message.ShowError("No item can be deleted.");
                return;
            }

            listView1.Items.RemoveAt(index);

            initListviewSelect();
            if (index <= listView1.Items.Count - 1)
                listView1.Items[index].Selected = true;
            else
                listView1.Items[listView1.Items.Count - 1].Selected = true;
            listView1.Focus();

            List<ListViewData> ItemsList = new List<ListViewData>();
            // Read data from list view
            for (int i = 0; i < listView1.Items.Count; i++)
            {
                String sn = listView1.Items[i].SubItems[0].Text;
                String snkey = listView1.Items[i].SubItems[1].Text;
                ListViewData data = new ListViewData();
                data.SerNum = sn;
                data.Key = snkey;
                ItemsList.Add(data);
            }
            SaveToFile(ItemsList);
        }

        private void button_apply_Click(object sender, EventArgs e)
        {
            if (listView1.FocusedItem == null || listView1.FocusedItem.Index < 0)
            {
                Message.ShowError("No item can apply.");
                return;
            }
            String sn = listView1.Items[listView1.FocusedItem.Index].SubItems[0].Text;
            String key = listView1.Items[listView1.FocusedItem.Index].SubItems[1].Text;
            String[] StrKey = key.Split(new char[] { ' ', ':', ';', '-', '_' });
            String status = String.Empty;
            Byte[] ByteKey = new Byte[12];

            for (int i = 0; i < StrKey.Length; i++)
            {
                try { ByteKey[i] = Convert.ToByte(StrKey[i], 16); }
                catch { ByteKey[i] = 0; }
            }
            Device.SetActivationKey(ByteKey);
            status = IsActivated ? "PASS!" : "FAILED!";
            toolStripStatusLabel1.Text = "Device (" + sn + ") key applies " + status;
            if (!IsActivated)
                Message.ShowError("The key applies FAILED!\n\n" +
                                     "Please check relevant information.");
        }
        public bool IsActivated { get { if (Device.GetActivationResult() == 1) return true; else return false; } }
        private void initListviewSelect()
        {
            for(int i=0;i<listView1.Items.Count;i++)
            {
                listView1.Items[i].Selected = false;
            }
        }
    }
}
