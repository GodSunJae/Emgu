using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Diagnostics;
using Emgu.CV.Util;

namespace ovenC
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        Image<Bgr, byte> img;
        private void button1_Click(object sender, EventArgs e)
        {
            //단일쓰레드 일때 문자열을 많이 읽고 변경할경우 좋은 StringBuilder 사용 ( string 비해 속도 매우빠름)
            // https://blog.naver.com/impressives2/221338797755 참고
            StringBuilder msgBuilder = new StringBuilder("Performance: ");


            //Image<Bgr, Byte> img =
            //  new Image<Bgr, byte>(fileNameTextBox.Text)
            //  .Resize(400, 400, Emgu.CV.CvEnum.Inter.Linear, true);


            // OpenFileDialog 으로 image를 가지고와야 하므로 객체생성
            OpenFileDialog ofd = new OpenFileDialog();
            //// OpenFileDialog 상자가 열리고 확인버튼을 눌렀을 때 !!
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                //https://dic1224.blog.me/220841161411  Resize는 확대축소
                //https://blog.naver.com/PostView.nhn?blogId=dic1224&logNo=220841171866&parentCategoryNo=&categoryNo=152&viewDate=&isShowPopularPosts=true&from=search 
                //위에꺼는 소스와 그림(?)
                //https://dic1224.blog.me/220841161411 Emgu.CV.CvEnum.Inter.Linear의 구조
                img = new Image<Bgr, Byte>(ofd.FileName).Resize(400, 400, Emgu.CV.CvEnum.Inter.Linear, true);
            }
            UMat uimage = new UMat();

            // img 객체 이미지가 Bgr 형식으로 되어있으니 Bgr2Gray로 그레이시킨 후 uimage에 값을 출력
            CvInvoke.CvtColor(img, uimage, ColorConversion.Bgr2Gray);


     
            //use image pyr to remove noise
            UMat pyrDown = new UMat();
            CvInvoke.PyrDown(uimage, pyrDown); // 노이즈제거 및 그레이된걸 샘플링다운으로 추출 (출력..?)
            CvInvoke.PyrUp(pyrDown, uimage); // 노이즈제거 후 샘플링업으로 추출( 출력..)

            Image<Gray, Byte> gray = img.Convert<Gray, Byte>().PyrDown().PyrUp();

            #region circle detection
            // 경과시간을 정확히 추출하는 객체 하나생성 ( 0초로) 시간을 재야함
            Stopwatch watch = Stopwatch.StartNew();

            //원형 만들기 위한 수치
            double cannyThreshold = 180.0;
            double circleAccumulatorThreshold = 120;

            //uimage는 노이즈제거, 그레이, 샘플링다운상태
            //uimage로 원그리기
            CircleF[] circles = CvInvoke.HoughCircles(uimage, HoughType.Gradient, 2.0, 20.0, cannyThreshold, circleAccumulatorThreshold, 5);
            
            // 원다그렸으니 시간을 멈추기
            watch.Stop();

            // 얼마나 걸렸는지 출력
            msgBuilder.Append(String.Format("Hough circles - {0} ms; ", watch.ElapsedMilliseconds));
            #endregion

            #region Canny and edge detection
            // 0초로만들고 다시시작
            watch.Reset(); watch.Start();
            double cannyThresholdLinking = 120.0;

            UMat cannyEdges = new UMat();
            //Canny알고리즘사용하여 cannyEdges 객체에 값넣기 3,4번째 임계값1,2
            CvInvoke.Canny(uimage, cannyEdges, cannyThreshold, cannyThresholdLinking);

           
            //cannyEdges 한 후 Hough lines한다
            LineSegment2D[] lines = CvInvoke.HoughLinesP(
               cannyEdges,
               1, //Distance resolution in pixel-related units
               Math.PI / 45.0, //Angle resolution measured in radians.
               20, //threshold
               30, //min Line width
               10); //gap between lines

            // 측정을멈춤
            watch.Stop();
            //몇초 걸렸는지 출력
            msgBuilder.Append(String.Format("Canny & Hough lines - {0} ms; ", watch.ElapsedMilliseconds));
            #endregion

            #region Find triangles and rectangles
            // 새로운 테스트위해 시간을 0초로 되돌리고 시작
            watch.Reset(); watch.Start();

            // triangles 객체생성
            List<Triangle2DF> triangleList = new List<Triangle2DF>();
            // rectangles 객체생성
            List<RotatedRect> boxList = new List<RotatedRect>(); //a box is a rotated rectangle


            using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
            {
                CvInvoke.FindContours(cannyEdges, contours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple);
                int count = contours.Size;
                for (int i = 0; i < count; i++)
                {
                    using (VectorOfPoint contour = contours[i])
                    using (VectorOfPoint approxContour = new VectorOfPoint())
                    {
                        CvInvoke.ApproxPolyDP(contour, approxContour, CvInvoke.ArcLength(contour, true) * 0.05, true);
                        if (CvInvoke.ContourArea(approxContour, false) > 100) //only consider contours with area greater than 250
                        {
                            if (approxContour.Size == 3) //The contour has 3 vertices, it is a triangle
                            {
                                Point[] pts = approxContour.ToArray();
                                triangleList.Add(new Triangle2DF(
                                   pts[0],
                                   pts[1],
                                   pts[2]
                                   ));
                            }
                            else if (approxContour.Size == 4) //The contour has 4 vertices.
                            {
                                #region determine if all the angles in the contour are within [80, 100] degree
                                bool isRectangle = true;
                                Point[] pts = approxContour.ToArray();
                                LineSegment2D[] edges = PointCollection.PolyLine(pts, true);

                                for (int j = 0; j < edges.Length; j++)
                                {
                                    double angle = Math.Abs(
                                       edges[(j + 1) % edges.Length].GetExteriorAngleDegree(edges[j]));
                                    if (angle < 90 || angle > 110)
                                    {
                                        isRectangle = false;
                                        break;
                                    }
                                }
                                #endregion

                                if (isRectangle) boxList.Add(CvInvoke.MinAreaRect(approxContour));
                            }
                        }
                    }
                }
            }
            //측정종료
            watch.Stop();
            //출력
            msgBuilder.Append(String.Format("Triangles & Rectangles - {0} ms; ", watch.ElapsedMilliseconds));
            #endregion

            // 원본 img는 그대로 pictureBox에 출력
            originalImageBox.Image = img;
            // 폼제목을 측정한 데이터로 변경
            this.Text = msgBuilder.ToString();

            #region draw triangles and rectangles
            Image<Bgr, Byte> triangleRectangleImage = img.CopyBlank();
            foreach (Triangle2DF triangle in triangleList)
                triangleRectangleImage.Draw(triangle, new Bgr(Color.DarkBlue), 2);
            foreach (RotatedRect box in boxList)
                triangleRectangleImage.Draw(box, new Bgr(Color.DarkOrange), 2);
            // triangles and rectangles 한 img를 pictureBox2에출력
            triangleRectangleImageBox.Image = triangleRectangleImage;
            #endregion

            Image<Bgr, Byte> circleImage = img.CopyBlank();
            #region draw circles  img.CopyBlank();
            foreach (CircleF circle in circles)
                circleImage.Draw(circle, new Bgr(Color.Brown), 2);
            // circles 한 img를 pictureBox3에출력
            circleImageBox.Image = circleImage;
            #endregion

            #region draw lines
            Image<Bgr, Byte> lineImage = img.CopyBlank();
            foreach (LineSegment2D line in lines)
                lineImage.Draw(line, new Bgr(Color.Green), 2);
            // Detected Lines 한 이미지를 pictureBox4에출력
            lineImageBox.Image = lineImage;
            #endregion
        }
    }
}
